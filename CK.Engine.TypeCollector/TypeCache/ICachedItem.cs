using CK.Core;
using System.Collections.Immutable;
using System.Reflection;
using System.Text;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Generalizes <see cref="ICachedType"/> and <see cref="CachedMember"/>.
/// </summary>
public interface ICachedItem
{
    /// <summary>
    /// Gets the name of this type or member.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the custom attributes data.
    /// <para>
    /// Instantiating attributes is more expensive that exploiting the <see cref="CustomAttributeData"/>
    /// but it requires more work and guards in the attribute constructor (if any) must be replicated:
    /// <see cref="RawAttributes"/> caches the instantiated attribute objects.
    /// </para>
    /// </summary>
    ImmutableArray<CustomAttributeData> AttributesData { get; }

    /// <summary>
    /// Gets the [<see cref="System.Attribute"/>] instantiated attribute objects.
    /// <para>
    /// This doesn't contain the resolved Engine side "AttributeImpl".
    /// </para>
    /// </summary>
    ImmutableArray<object> RawAttributes { get; }

    /// <summary>
    /// Gets the attributes where any <see cref="EngineAttribute"/>, <see cref="ChildEngineAttribute{T}"/> have been
    /// replaced with their initialized Engine side "AttributeImpl" peers.
    /// <para>
    /// This array has the same length and is in the same order as <see cref="RawAttributes"/>: the orignal Engine
    /// Attribute attributes at the same index are replaced by their peers.
    /// </para>
    /// </summary>
    /// <param name="monitor">Required monitor to log any peer instantiation error on the first call.</param>
    /// <param name="attributes">
    /// The regular attributes and the Engine side "AttributeImpl" peers.
    /// <see cref="ImmutableArray{T}.IsDefault"/> on error.
    /// </param>
    /// <returns>True on success, false on error.</returns>
    bool TryGetAllAttributes( IActivityMonitor monitor, out ImmutableArray<object> attributes );

    /// <summary>
    /// Gets the <see cref="GlobalTypeCache"/>.
    /// </summary>
    GlobalTypeCache TypeCache { get; }

    /// <summary>
    /// Writes the C# name of this item. 
    /// </summary>
    /// <param name="b">The target builder.</param>
    /// <returns>The builder.</returns>
    StringBuilder Write( StringBuilder b );
}
