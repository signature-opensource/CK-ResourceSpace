using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Setup;

/// <summary>
/// The exclusion of a type follows the Liskov substitution principle: excluding a type excludes every type that <b>is</b>
/// this type (an instance of a specialization is an instance of its base): the type itself, its specializations, the
/// implementations and the extensions of an interface. A generic type definition excludes all its constructed types and
/// what is one of them (<c>IRepository&lt;&gt;</c> excludes <c>IRepository&lt;User&gt;</c> and its implementations).
/// <para>
/// So no member of a set is assignable to one of its excluded types: the base types of a member need no rule of their
/// own. A type is either in or out with everything that is it; an exclusion never "hides" a base type from the types
/// that specialize it (this would be a mapping setting, not a membership).
/// </para>
/// </summary>
public static class TypeExclusion
{
    /// <summary>
    /// Gets whether <paramref name="excluded"/> excludes <paramref name="type"/>: the type is the excluded one, a
    /// specialization or an implementation of it (or of one of its constructed types when it is a generic type definition).
    /// A nullable value type is excluded when its underlying type is.
    /// </summary>
    /// <param name="excluded">The excluded type.</param>
    /// <param name="type">The type to test.</param>
    /// <returns>True if the type is excluded.</returns>
    public static bool Excludes( this Type excluded, Type type )
    {
        type = Nullable.GetUnderlyingType( type ) ?? type;
        if( !excluded.IsGenericTypeDefinition ) return excluded.IsAssignableFrom( type );
        if( IsConstructedFrom( type, excluded ) ) return true;
        if( excluded.IsInterface ) return type.GetInterfaces().Any( i => IsConstructedFrom( i, excluded ) );
        for( var b = type.BaseType; b != null; b = b.BaseType )
        {
            if( IsConstructedFrom( b, excluded ) ) return true;
        }
        return false;

        static bool IsConstructedFrom( Type t, Type definition ) => t == definition || (t.IsGenericType && t.GetGenericTypeDefinition() == definition);
    }

    /// <summary>
    /// Finds the first type among <paramref name="excludedTypes"/> that excludes <paramref name="type"/>
    /// (see <see cref="Excludes(Type, Type)"/>).
    /// </summary>
    /// <param name="excludedTypes">The excluded types.</param>
    /// <param name="type">The type to test.</param>
    /// <returns>The excluded type that excludes it or null.</returns>
    public static Type? FindExcluder( this IEnumerable<Type> excludedTypes, Type type )
    {
        foreach( var e in excludedTypes )
        {
            if( e.Excludes( type ) ) return e;
        }
        return null;
    }
}
