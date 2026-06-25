using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;

namespace CK.Core;

sealed class ResCoreDataCacheBuilder
{
    readonly ResCoreData _spaceData;
    // Contains AggregateKey to index mapping for local and stable.
    // Only AggregateKey for more than one package is computed. The AggregateId
    // (localKeyId and StableKeyId) for single package set is the single package identifier.
    // True AggregateId key (with at least 2 packages) start at the _totalPackageCount.
    readonly Dictionary<AggregateKey, int> _aggregateIndex;
    // Stores for true AggregateId keys.
    readonly List<AggregateKey> _stableAggregates;
    readonly List<AggregateKey> _localAggregates;
    // Reachable packages are deduplicated and shared instances are
    // exposed by ResPackage.Reachables.
    readonly Dictionary<AggregateId, HashSet<ResPackage>> _reachableIndex;
    // Reusable buffers (avoiding allocation).
    readonly int[] _localBuffer;
    readonly int[] _stableBuffer;
    // This considers "<App>" and "<Code>" packages, this is the ResPackageDataCache.DataCacheLength
    // and the offset of true AggregateId keys.
    readonly int _totalPackageCount;

    public ResCoreDataCacheBuilder( ResCoreData spaceData,
                                  int descriptorPackageCount,
                                  int collectorLocalPackageCount,
                                  bool appHasLocalPath )
    {
        _aggregateIndex = new Dictionary<AggregateKey, int>();
        _reachableIndex = new Dictionary<AggregateId, HashSet<ResPackage>>();
        _stableAggregates = new List<AggregateKey>();
        _localAggregates = new List<AggregateKey>();
        // We include the tail "<App>" package here regardless of whether a
        // path for the App has been provided because "<App>" is local dependent...
        // Except if we have no local package at all and no path for the App: in this
        // case we have absolutely NO local package!
        _localBuffer = new int[collectorLocalPackageCount != 0
                                ? collectorLocalPackageCount + 1
                                : appHasLocalPath
                                    ? 1
                                    : 0];
        // We include the head "<Code>" package here.
        _stableBuffer = new int[1 + descriptorPackageCount - collectorLocalPackageCount];
        // The space data is being built. We don't rely on it.
        // Consider "<App>" and "<Code>" packages. 
        _totalPackageCount = descriptorPackageCount + 2;
        _spaceData = spaceData;
    }

    public int TotalPackageCount => _totalPackageCount;

    public ResCoreData SpaceData => _spaceData;

    public IReadOnlySet<ResPackage> GetReachableClosure( IReadOnlyCollection<ResPackage> packages, out AggregateId aggregateId )
    {
        Throw.DebugAssert( packages.Count > 0 );
        aggregateId = RegisterAggregate( packages );
        if( !_reachableIndex.TryGetValue( aggregateId, out var exists ) )
        {
            var closure = new HashSet<ResPackage>();
            foreach( var p in packages )
            {
                if( closure.Add( p ) )
                {
                    closure.UnionWith( p.AfterReachables );
                }
            }
            exists = closure;
            _reachableIndex.Add( aggregateId, exists );
        }
        return exists;
    }

    public AggregateId RegisterAggregate( IReadOnlyCollection<ResPackage> packages )
    {
        int nbLocal = 0;
        int nbStable = 0;
        foreach( var p in packages )
        {
            if( p.IsEventuallyLocalDependent )
            {
                _localBuffer[nbLocal++] = p.Index;
            }
            else
            {
                _stableBuffer[nbStable++] = p.Index;
            }
        }
        var localAggregateId = nbLocal switch
        {
            0 => 0,
            1 => _localBuffer[0] + 1,
            _ => FindOrCreateLocal( nbLocal ),
        };
        var stableAggregateId = nbStable switch
        {
            0 => 0,
            1 => _stableBuffer[0] + 1,
            _ => FindOrCreateStable( nbStable ),
        };
        return new AggregateId( localAggregateId, stableAggregateId );
    }

    int FindOrCreateStable( int nbStable )
    {
        var key = new AggregateKey( _stableBuffer.AsSpan( 0, nbStable ) );
        if( !_aggregateIndex.TryGetValue( key, out var id ) )
        {
            // Add first: offsets the id by 1.
            _stableAggregates.Add( key );
            id = _totalPackageCount + _stableAggregates.Count;
            _aggregateIndex.Add( key, id );
        }
        return id;
    }

    int FindOrCreateLocal( int nbLocal )
    {
        var key = new AggregateKey( _localBuffer.AsSpan( 0, nbLocal ) );
        if( !_aggregateIndex.TryGetValue( key, out var id ) )
        {
            _localAggregates.Add( key );
            id = _totalPackageCount + _localAggregates.Count;
            _aggregateIndex.Add( key, id );
        }
        return id;
    }

    public ResCoreDataCache Build( IActivityMonitor monitor, ImmutableArray<ResPackage> packages, bool withLiveState )
    {
        CheckInvariant();
        // Computes the stable identifiers.
        HashSet<int>? stableIdentifiers = null;
        List<IResPackageResources>?[]? impactLists = null;
        if( withLiveState )
        {
            // The <App> may not be IsLocalPackage but if we are here (because there's a watch root), then
            // there's at least one local package and the <App> is necessarily local dependent.
            Throw.DebugAssert( _spaceData.AppPackage.IsEventuallyLocalDependent );
            stableIdentifiers = new HashSet<int>();
            foreach( var p in _spaceData.LocalPackages )
            {
                Throw.DebugAssert( p.IsLocalPackage );
                var (requiresAggregateId, childrenAggregateId) = p.GetAggregateIdentifiers();
                // This may be a single package identifier (offset by 1)
                // or a aggregate identifier (greater than total package count).
                if( requiresAggregateId.HasStable )
                {
                    stableIdentifiers.Add( requiresAggregateId._stableKeyId );
                }
                if( childrenAggregateId.HasStable )
                {
                    stableIdentifiers.Add( childrenAggregateId._stableKeyId );
                }
            }
            monitor.Debug( $"Optimal Stable Aggregated Data set has {stableIdentifiers.Count} cache entries for {_spaceData.LocalPackages.Length} local data." );

            // No need to hanlde the <Code>: it is not local dependent.
            // Even if <App> is always impacted by design, we handle it as a regular local dependent package.
            // This reversed list is not a closure: we only track one level of impacts so we have
            // the shortest possible lists to marshall. The ResPackageDataCache follows the links.
            impactLists = new List<IResPackageResources>?[_totalPackageCount - 1];
            foreach( var p in _spaceData.Packages )
            {
                // No need to skip the first <Code> as it is not local dependent.
                if( p.IsEventuallyLocalDependent )
                {
                    foreach( ResPackage source in p.Requires )
                    {
                        if( source.IsEventuallyLocalDependent )
                        {
                            AddImpact( impactLists, source, p.Resources );
                        }
                    }
                    foreach( var source in p.Children )
                    {
                        if( source.IsEventuallyLocalDependent )
                        {
                            AddImpact( impactLists, source, p.AfterResources );
                        }
                    }
                }
            }
        }
        return new ResCoreDataCache( packages, _localAggregates, _stableAggregates, stableIdentifiers, impactLists );
    }

    void AddImpact( List<IResPackageResources>?[] impactLists, ResPackage p, IResPackageResources impact )
    {
        var impacts = impactLists[p.Index - 1] ??= new List<IResPackageResources>();
        impacts.Add( impact );
    }

    [Conditional( "DEBUG" )]
    public void CheckInvariant()
    {
        Throw.DebugAssert( _totalPackageCount == _spaceData.Packages.Length );
        Throw.DebugAssert( _totalPackageCount * 2 == _spaceData.AllPackageResources.Length );
        foreach( var p in _spaceData.Packages )
        {
            var (requiresAggregateId, childrenAggregateId) = p.GetAggregateIdentifiers();

            var localRequires = p.Requires.Where( p => p.IsEventuallyLocalDependent ).ToHashSet();
            Throw.DebugAssert( localRequires.SetEquals( GetLocalSources( requiresAggregateId ) ) );

            var localChildren = p.Children.Where( p => p.IsEventuallyLocalDependent ).ToHashSet();
            Throw.DebugAssert( localChildren.SetEquals( GetLocalSources( childrenAggregateId ) ) );
        }

        IEnumerable<ResPackage> GetLocalSources( AggregateId aggregateId )
        {
            if( aggregateId.HasLocal )
            {
                var id = aggregateId._localKeyId - 1;
                var trueAggregateId = id - _totalPackageCount;
                if( trueAggregateId < 0 )
                {
                    // We depend on a single local package.
                    var local = _spaceData.Packages[id];
                    Throw.DebugAssert( local.IsEventuallyLocalDependent );
                    yield return local;
                }
                else
                {
                    // Instance of type 'System.ReadOnlySpan<int>' cannot be preserved across 'await' or 'yield' boundary.
                    var packageIds = _localAggregates[trueAggregateId].PackageIndexes.ToArray();
                    foreach( var pId in packageIds )
                    {
                        var local = _spaceData.Packages[pId];
                        Throw.DebugAssert( local.IsEventuallyLocalDependent );
                        yield return local;
                    }
                }
            }
        }
    }

}
