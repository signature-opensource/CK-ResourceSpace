using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Cached constructor info.
/// </summary>
public sealed class CachedConstructor : CachedMethodBase
{
    internal CachedConstructor( ICachedType declaringType, ConstructorInfo ctor )
        : base( declaringType, ctor )
    {
    }

    /// <summary>
    /// Gets the <see cref="ConstructorInfo"/>.
    /// </summary>
    public ConstructorInfo ConstructorInfo => Unsafe.As<ConstructorInfo>( _member );

    internal override StringBuilder Write( StringBuilder b, bool withDeclaringType )
    {
        b.Append( withDeclaringType ? DeclaringType.CSharpName : DeclaringType.Name ).Append( '(' );
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
