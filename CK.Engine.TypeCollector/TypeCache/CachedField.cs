using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Cached <see cref="FieldInfo"/>.
/// </summary>
public sealed class CachedField : CachedMember
{
    ICachedType? _type;

    internal CachedField( ICachedType declaringType, FieldInfo prop )
        : base( declaringType, prop )
    {
    }

    /// <summary>
    /// Gets the type of the field.
    /// </summary>
    public ICachedType FieldType => _type ??= TypeCache.Get( FieldInfo.FieldType );

    /// <summary>
    /// Gets the <see cref="FieldInfo"/>.
    /// </summary>
    public FieldInfo FieldInfo => Unsafe.As<FieldInfo>( _member );

    internal override StringBuilder Write( StringBuilder b, bool withDeclaringType )
    {
        FieldType.Write(  b );
        b.Append( ' ' );
        if( withDeclaringType ) b.Append( DeclaringType.CSharpName ).Append( '.' );
        b.Append( Name );
        return b;
    }
}
