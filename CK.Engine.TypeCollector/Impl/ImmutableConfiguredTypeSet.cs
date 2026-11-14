using CK.Setup;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CK.Engine.TypeCollector;

/// <summary>
/// A set of types without configured kinds, that doesn't change: public for the engine's projects (CK.Engine.TypeSet).
/// </summary>
public sealed class ImmutableConfiguredTypeSet : IConfiguredTypeSet
{
    /// <summary>
    /// The empty set.
    /// </summary>
    public static readonly ImmutableConfiguredTypeSet Empty = new ImmutableConfiguredTypeSet();

    readonly IReadOnlyCollection<TypeConfiguration> _configuredTypes;

    readonly IReadOnlySet<ICachedType> _allTypes;

    /// <summary>
    /// Initializes a set on a hash set that must not change anymore (it is not copied).
    /// </summary>
    /// <param name="types">The types.</param>
    public ImmutableConfiguredTypeSet( HashSet<ICachedType> types )
    {
        _allTypes = types;
        _configuredTypes = ImmutableArray<TypeConfiguration>.Empty;
    }

    // Empty.
    ImmutableConfiguredTypeSet()
    {
        _allTypes = ImmutableHashSet<ICachedType>.Empty;
        _configuredTypes = ImmutableArray<TypeConfiguration>.Empty;
    }

    /// <inheritdoc />
    public IReadOnlySet<ICachedType> AllTypes => _allTypes;

    /// <inheritdoc />
    public IReadOnlyCollection<TypeConfiguration> ConfiguredTypes => _configuredTypes;
}
