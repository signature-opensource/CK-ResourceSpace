using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Base cached item implementation. Applies to all <see cref="CachedMember"/> but
/// not to all <see cref="ICachedType"/> implementations.
/// <para>
/// Should not be used directly: use <see cref="ICachedItem"/> instead.
/// </para>
/// </summary>
public abstract partial class CachedItem : ICachedItem
{
    private protected readonly MemberInfo _member;
    ImmutableArray<CustomAttributeData> _customAttributes;
    ImmutableArray<object> _attributes;
    ImmutableArray<object> _finalAttributes;
    bool _finalAttributesInitialized;

    internal CachedItem( MemberInfo member )
    {
        _member = member;
    }

    /// <inheritdoc />
    public string Name => _member.Name;

    /// <inheritdoc />
    public ImmutableArray<CustomAttributeData> AttributesData
    {
        get
        {
            if( _customAttributes.IsDefault )
            {
                // Cannot use ImmutableCollectionsMarshal.AsImmutableArray here: CustomAttributeData
                // can be retrieved by IList<CustomAttributeData> or IEnumerable<CustomAttributeData> only.
                _customAttributes = _member.CustomAttributes.ToImmutableArray();
            }
            return _customAttributes;
        }
    }


    /// <inheritdoc />
    public ImmutableArray<object> RawAttributes
    {
        get
        {
            if( _attributes.IsDefault )
            {
                _attributes = ImmutableCollectionsMarshal.AsImmutableArray( _member.GetCustomAttributes( inherit: false ) );
            }
            return _attributes;
        }
    }

    /// <inheritdoc />
    public abstract GlobalTypeCache TypeCache { get; }

    /// <inheritdoc />
    public abstract StringBuilder Write( StringBuilder b );
}
