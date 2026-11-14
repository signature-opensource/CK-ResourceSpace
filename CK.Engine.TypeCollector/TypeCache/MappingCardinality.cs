namespace CK.Engine.TypeCollector;

/// <summary>
/// The mapping cardinality of a type: whether an abstraction is mapped to at most one implementation
/// or to all of them. See <see cref="CachedTypeMapping.Cardinality"/>.
/// </summary>
public enum MappingCardinality : byte
{
    /// <summary>
    /// The type has no mapping cardinality.
    /// </summary>
    None,

    /// <summary>
    /// At most one implementation is mapped (declared by a <see cref="CK.Core.ISingleMappingAttribute"/>
    /// or inherited).
    /// </summary>
    Single,

    /// <summary>
    /// All the implementations are mapped (declared by a <see cref="CK.Core.IMultipleMappingAttribute"/>
    /// or inherited).
    /// </summary>
    Multiple
}
