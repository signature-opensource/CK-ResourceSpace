using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CK.Core;

interface IInternalCoreDataCache : ICoreDataCache
{
    int StableAggregateCacheLength { get; }
    int LocalAggregateCacheLength { get; }
    ImmutableArray<ResPackage> Packages { get; }

    /// <summary>
    /// Gets the package identifiers that compose a stable aggregate by
    /// its 0-based index.
    /// </summary>
    /// <param name="trueAggregateId">0-based stable aggregate identifier.</param>
    /// <returns>The package identifiers.</returns>
    ReadOnlySpan<int> GetStableAggregate( int trueAggregateId );

    /// <summary>
    /// Gets the package identifiers that compose a local dependent aggregate by
    /// its 0-based index.
    /// </summary>
    /// <param name="trueAggregateId">0-based local aggregate identifier.</param>
    /// <returns>The package identifiers.</returns>
    ReadOnlySpan<int> GetLocalAggregate( int trueAggregateId );

    /// <summary>
    /// Empty when <see cref="ResCoreData.WatchRoot"/> is null: this is for the Live state only.
    /// <para>
    /// <see cref="ResPackageDataCache{T}"/> uses this to save the data associated
    /// to all the stable packages so Live doesn't need to load not local assemblies
    /// to re-create the associated data.
    /// </para>
    /// <para>
    /// The data associated to stable aggregates are saved but not all
    /// them: only the ones directly used by a local package (stable
    /// aggregates used only by stable packages are used once at initialization
    /// time and don't need to be restored on the live side).
    /// </para>
    /// </summary>
    IReadOnlyCollection<int> StableIdentifiers { get; }

    /// <summary>
    /// Gets the direct referencers of a local dependent package.
    /// Must not be called for "&lt;Code&gt;" (that is not local dependent by design)
    /// nor "&lt;App&gt;" (that is always local dependent).
    /// </summary>
    /// <param name="p">The package.</param>
    /// <returns>The package resources that depends on <paramref name="p"/>.</returns>
    ReadOnlySpan<IResPackageResources> GetImpacts( ResPackage p );

}
