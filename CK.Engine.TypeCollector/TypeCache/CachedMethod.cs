using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Cached <see cref="MethodInfo"/>.
/// </summary>
public sealed class CachedMethod : CachedMethodBase
{
    CachedParameter? _returnParameter;
    MethodInfo? _baseMethodDefinition;

    internal CachedMethod( ICachedType declaringType, MethodInfo method )
        : base( declaringType, method )
    {
    }

    /// <summary>
    /// Gets whether this is a static method.
    /// </summary>
    public bool IsStatic => MethodInfo.IsStatic;

    /// <summary>
    /// Gets the cached info. Should rarely be used directly.
    /// </summary>
    public MethodInfo MethodInfo => Unsafe.As<MethodInfo>( _member );

    /// <summary>
    /// Gets the return parameter.
    /// </summary>
    public CachedParameter ReturnParameter => _returnParameter ??= new CachedParameter( this, MethodInfo.ReturnParameter );

    MethodInfo? BaseMethodDefinition => _baseMethodDefinition ??= MethodInfo.GetBaseDefinition();

    /// <summary>
    /// Gets whether this method is virtual and overrides a base method.
    /// </summary>
    public bool IsOverride => BaseMethodDefinition != _member;

    /// <summary>
    /// Gets whether this method is overridden by the <paramref name="candidate"/>.
    /// </summary>
    /// <param name="candidate">The potential override </param>
    /// <returns>Whether this method is overridden by the candidate.</returns>
    public bool IsOverriddenBy( CachedMethod candidate )
    {
        return candidate.DeclaringType.TypeDepth > DeclaringType.TypeDepth
                && BaseMethodDefinition == candidate.BaseMethodDefinition;
    }


    /// <summary>
    /// Gets whether this method returns a <see cref="GlobalTypeCache.WellKnownTypes.Task"/>,
    /// <see cref="GlobalTypeCache.WellKnownTypes.GenericTaskDefinition"/>, <see cref="GlobalTypeCache.WellKnownTypes.ValueTask"/>
    /// or a <see cref="GlobalTypeCache.WellKnownTypes.GenericValueTaskDefinition"/>.
    /// </summary>
    public bool IsAsynchronous
    {
        get
        {
            var r = ReturnParameter.ParameterType;
            var knownTypes = TypeCache.KnownTypes;
            return r == knownTypes.Task
                   || r == knownTypes.ValueTask
                   || (r.IsGenericType && (r.GenericTypeDefinition == knownTypes.GenericTaskDefinition
                                           || r.GenericTypeDefinition == knownTypes.GenericValueTaskDefinition));
        }
    }

    /// <summary>
    /// Unwraps the type T from the Task&lt;T&gt; or ValueTask&lt;T&gt; or returns <see cref="GlobalTypeCache.WellKnownTypes.Void"/>
    /// for non generic Task and ValueTask.
    /// <para>
    /// This is null if this method is not an asynchronous method.
    /// </para>
    /// </summary>
    /// <returns>The unwrapped type or null for a synchronous method.</returns>
    public ICachedType? GetAsynchronousReturnedType()
    {
        var r = ReturnParameter.ParameterType;
        var knownTypes = TypeCache.KnownTypes;
        if( r == knownTypes.Task || r == knownTypes.ValueTask )
        {
            return knownTypes.Void;
        }
        if( r.IsGenericType && (r.GenericTypeDefinition == knownTypes.GenericTaskDefinition
                                || r.GenericTypeDefinition == knownTypes.GenericValueTaskDefinition) )
        {
            return r.GenericArguments[0];
        }
        return null;
    }

    internal override StringBuilder Write( StringBuilder b, bool withDeclaringType )
    {
        if( MethodInfo.IsStatic ) b.Append( "static " );
        ReturnParameter.Write( b );
        b.Append( ' ' );
        if( withDeclaringType ) b.Append( DeclaringType.CSharpName ).Append( '.' );
        b.Append( Name ).Append('(');
        int i = 0;
        foreach( var p in ParameterInfos )
        {
            if( i++ > 0 ) b.Append( ',' );
            b.Append( ' ' );
            p.Write( b );
        }
        if( i > 0 ) b.Append( ' ' );
        b.Append( ')' );
        return b;
    }
}
