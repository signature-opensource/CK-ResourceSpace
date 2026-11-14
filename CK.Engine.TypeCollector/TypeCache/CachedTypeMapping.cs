using CK.Core;
using System.Collections.Immutable;

namespace CK.Engine.TypeCollector;

/// <summary>
/// The mapping information of a type, obtained by <see cref="ICachedType.TryGetMapping(IActivityMonitor, out CachedTypeMapping?)"/>.
/// <para>
/// This is structural: it depends only on the type, its attributes (<see cref="ISingleMappingAttribute"/>, <see cref="IMultipleMappingAttribute"/>
/// and <see cref="INotMappedAttribute"/>) and its base types. Engines use it to map the implementations they select to their abstractions:
/// the mapping is automatic, the membership is not.
/// </para>
/// </summary>
public sealed class CachedTypeMapping
{
    /// <summary>
    /// The mapping of a type without cardinality, not mapped to any abstraction.
    /// </summary>
    public static readonly CachedTypeMapping Empty = new CachedTypeMapping( MappingCardinality.None, false, false, [], [] );

    internal CachedTypeMapping( MappingCardinality cardinality,
                                bool isNotMapped,
                                bool isKey,
                                ImmutableArray<ICachedType> singleKeys,
                                ImmutableArray<ICachedType> multipleKeys )
    {
        Cardinality = cardinality;
        IsNotMapped = isNotMapped;
        IsKey = isKey;
        SingleKeys = singleKeys;
        MultipleKeys = multipleKeys;
    }

    /// <summary>
    /// Gets the cardinality declared by the type or inherited from its base types: when a base type is <see cref="MappingCardinality.Single"/>,
    /// this is <see cref="MappingCardinality.Single"/> (there cannot be multiple implementations of an abstraction that has at most one).
    /// <para>
    /// A type that <see cref="IsNotMapped"/> carries it: its specializations inherit it.
    /// </para>
    /// </summary>
    public MappingCardinality Cardinality { get; }

    /// <summary>
    /// Gets whether an <see cref="INotMappedAttribute"/> decorates the type itself: it is not a mapping key and it is
    /// not mapped to its abstractions. Its specializations are not concerned.
    /// </summary>
    public bool IsNotMapped { get; }

    /// <summary>
    /// Gets whether this type is an abstraction to which implementations are mapped: an interface or an abstract class
    /// (that is not a generic type definition) with a <see cref="Cardinality"/> that is not <see cref="IsNotMapped"/>.
    /// </summary>
    public bool IsKey { get; }

    /// <summary>
    /// Gets the single keys this type is mapped to. Empty unless this type is a concrete class that is not <see cref="IsNotMapped"/>.
    /// </summary>
    public ImmutableArray<ICachedType> SingleKeys { get; }

    /// <summary>
    /// Gets the multiple keys this type is mapped to. Empty unless this type is a concrete class that is not <see cref="IsNotMapped"/>.
    /// </summary>
    public ImmutableArray<ICachedType> MultipleKeys { get; }
}
