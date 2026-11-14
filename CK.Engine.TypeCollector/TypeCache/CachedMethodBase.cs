using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Generalizes <see cref="CachedMethod"/> and <see cref="CachedConstructor"/>.
/// </summary>
public abstract class CachedMethodBase : CachedMember
{
    ImmutableArray<CachedParameter> _parameterInfos;

    internal CachedMethodBase( ICachedType declaringType, MethodBase method )
        : base( declaringType, method )
    {
    }

    /// <summary>
    /// Gets the cached info. Should rarely be used directly.
    /// </summary>
    public MethodBase MethodBase => Unsafe.As<MethodBase>( _member );

    /// <summary>
    /// Gets whether this is a public method.
    /// </summary>
    public bool IsPublic => MethodBase.IsPublic;

    /// <summary>
    /// Gets the parameters.
    /// </summary>
    public ImmutableArray<CachedParameter> ParameterInfos
    {
        get
        {
            if( _parameterInfos.IsDefault )
            {
                var parameters = MethodBase.GetParameters();
                var b = ImmutableArray.CreateBuilder<CachedParameter>( parameters.Length );
                foreach( var p in parameters ) b.Add( new CachedParameter( this, p ) );
                _parameterInfos = b.MoveToImmutable();
            }
            return _parameterInfos;
        }
    }

    internal void WriteParameters( StringBuilder b )
    {
        b.Append( '(' );
        int i = 0;
        foreach( var p in ParameterInfos )
        {
            if( i++ > 0 ) b.Append( ',' );
            b.Append( ' ' );
            p.Write( b );
        }
        if( i > 0 ) b.Append( ' ' );
        b.Append( ')' );
    }
}
