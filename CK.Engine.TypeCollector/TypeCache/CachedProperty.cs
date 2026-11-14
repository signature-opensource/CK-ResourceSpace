using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Cached <see cref="PropertyInfo"/>.
/// </summary>
public sealed class CachedProperty : CachedMember
{
    ICachedType? _type;

    internal CachedProperty( ICachedType declaringType, PropertyInfo prop )
        : base( declaringType, prop )
    {
    }

    /// <summary>
    /// Gets the type of this property.
    /// </summary>
    public ICachedType PropertyType => _type ??= TypeCache.Get( PropertyInfo.PropertyType );

    /// <summary>
    /// Gets the cached info. Should rarely be used directly.
    /// </summary>
    public PropertyInfo PropertyInfo => Unsafe.As<PropertyInfo>( _member );

    internal override StringBuilder Write( StringBuilder b, bool withDeclaringType )
    {
        PropertyType.Write( b );
        b.Append( ' ' );
        if( withDeclaringType ) b.Append( DeclaringType.CSharpName ).Append( '.' );
        b.Append( Name );
        return b;
    }
}
