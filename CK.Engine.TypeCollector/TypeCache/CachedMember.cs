using System.Reflection;
using System.Text;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Cached type member is a <see cref="CachedEvent"/>, <see cref="CachedField"/>,
/// <see cref="CachedProperty"/> or a <see cref="CachedMethodBase"/> that can be a <see cref="CachedConstructor"/>
/// or a <see cref="CachedMethod"/>.
/// <para>
/// This differs from the .Net reflection model: a <see cref="ICachedType"/> is not a <see cref="CachedMember"/>:
/// <see cref="ICachedItem"/> generalizes them.
/// </para>
/// </summary>
public abstract class CachedMember : CachedItem
{
    readonly ICachedType _declaringType;
    string? _toString;
    string? _toStringWithDeclaringType;

    internal CachedMember( ICachedType declaringType, MemberInfo member )
        : base( member )
    {
        _declaringType = declaringType;
    }

    /// <summary>
    /// Gets the type that declares this member.
    /// </summary>
    public ICachedType DeclaringType => _declaringType;

    /// <summary>
    /// Gets the cached <see cref="MemberInfo"/>.
    /// </summary>
    public MemberInfo Member => _member;

    /// <inheritdoc />
    public override sealed StringBuilder Write( StringBuilder b ) => Write( b, false );

    internal abstract StringBuilder Write( StringBuilder b, bool withDeclaringType );

    /// <inheritdoc />
    public override sealed GlobalTypeCache TypeCache => _declaringType.TypeCache;

    /// <summary>
    /// Returns a readable name with the declaring type.
    /// </summary>
    /// <returns>The readable member name.</returns>
    public string ToStringWithDeclaringType() => _toStringWithDeclaringType ??= Write( new StringBuilder(), true ).ToString();

    /// <summary>
    /// Returns a readable name without the declaring type.
    /// </summary>
    /// <returns>The readable member name.</returns>
    public override sealed string ToString() => _toString ??= Write( new StringBuilder(), false ).ToString();
}
