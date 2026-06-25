using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CK.Core;

/// <summary>
/// SpaceDataCache implementation in the Live world.
/// </summary>
sealed class LiveSpaceDataCache : IInternalCoreDataCache
{
    readonly ImmutableArray<ResPackage> _packages;
    readonly ImmutableArray<ImmutableArray<int>> _localAggregates;
    readonly ImmutableArray<ImmutableArray<int>> _stableAggregates;
    readonly int[] _stableIdentifiers;
    readonly IResPackageResources[]?[] _impacts;

    LiveSpaceDataCache( ImmutableArray<ResPackage> packages,
                        ImmutableArray<ImmutableArray<int>> localAggregates,
                        ImmutableArray<ImmutableArray<int>> stableAggregates,
                        int[] stableIdentifiers,
                        IResPackageResources[]?[] impacts )
    {
        _packages = packages;
        _localAggregates = localAggregates;
        _stableAggregates = stableAggregates;
        _stableIdentifiers = stableIdentifiers;
        _impacts = impacts;
    }

    public static LiveSpaceDataCache Read( ICKBinaryReader r,
                                           ImmutableArray<ResPackage> packages,
                                           ImmutableArray<IResPackageResources> allPackageResources )
    {
        var local = ReadAggregateKeys( r );
        var stable = ReadAggregateKeys( r );

        int[] stableIdentifiers = new int[r.ReadNonNegativeSmallInt32()];
        for( int i = 0; i < stableIdentifiers.Length; ++i )
        {
            stableIdentifiers[i] = r.ReadInt32();
        }
        IResPackageResources[]?[] impacts;
        var nbImpact = r.ReadNonNegativeSmallInt32();
        if( nbImpact == 0 )
        {
            impacts = [];
        }
        else
        {
            impacts = new IResPackageResources[]?[ nbImpact ];
            for( int i = 0; i < impacts.Length; i++ )
            {
                int c = r.ReadSmallInt32();
                Throw.DebugAssert( c == -1 || c > 0 );
                if( c != -1 )
                {
                    var res = new IResPackageResources[c];
                    for(int j = 0; j < res.Length; ++j )
                    {
                        res[j] = allPackageResources[r.ReadNonNegativeSmallInt32()];
                    }
                }
            }
        }

        return new LiveSpaceDataCache( packages, local, stable, stableIdentifiers, impacts );

        static ImmutableArray<ImmutableArray<int>> ReadAggregateKeys( ICKBinaryReader r )
        {
            var keys = new ImmutableArray<int>[r.ReadNonNegativeSmallInt32()];
            for( int i = 0; i < keys.Length; ++i )
            {
                var indexes = new int[r.ReadNonNegativeSmallInt32()];
                for( int j = 0; j < indexes.Length; ++j )
                {
                    indexes[j] = r.ReadInt32();
                }
                keys[i] = ImmutableCollectionsMarshal.AsImmutableArray( indexes );
            }
            return ImmutableCollectionsMarshal.AsImmutableArray( keys );
        }
    }

    void ICoreDataCache.LocalImplementationOnly() { }

    public int StableAggregateCacheLength => _stableAggregates.Length;

    public int LocalAggregateCacheLength => _localAggregates.Length;

    public ImmutableArray<ResPackage> Packages => _packages;

    public IReadOnlyCollection<int> StableIdentifiers => _stableIdentifiers;

    public ReadOnlySpan<int> GetStableAggregate( int trueAggregateId )
    {
        Throw.DebugAssert( trueAggregateId >= 0 && trueAggregateId < _stableAggregates.Length );
        return _stableAggregates[trueAggregateId].AsSpan();
    }

    public ReadOnlySpan<int> GetLocalAggregate( int trueAggregateId )
    {
        Throw.DebugAssert( trueAggregateId >= 0 && trueAggregateId < _localAggregates.Length );
        return _localAggregates[trueAggregateId].AsSpan();
    }

    public ReadOnlySpan<IResPackageResources> GetImpacts( ResPackage package )
    {
        int idx = package.Index - 1;
        Throw.DebugAssert( "Must not ask for <Code> nor <App> impacts.", idx >= 0 && idx < _impacts.Length );
        return _impacts[idx].AsSpan();
    }

}
