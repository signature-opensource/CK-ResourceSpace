using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CK.Core;

/// <summary>
/// Root SpaceDataCache implementation in engine world.
/// <para>
/// This is written in the Live state and read back by the <see cref="LiveSpaceDataCache"/>.
/// </para>
/// </summary>
sealed class ResCoreDataCache : IInternalCoreDataCache
{
    readonly ImmutableArray<ResPackage> _packages;
    readonly List<AggregateKey> _localAggregates;
    readonly List<AggregateKey> _stableAggregates;
    readonly IReadOnlyCollection<int> _stableIdentifiers;
    readonly IReadOnlyList<IReadOnlyCollection<IResPackageResources>?> _impacts;

    internal ResCoreDataCache( ImmutableArray<ResPackage> packages,
                               List<AggregateKey> localAggregates,
                               List<AggregateKey> stableAggregates,
                               IReadOnlyCollection<int>? stableIdentifiers,
                               IReadOnlyList<IReadOnlyCollection<IResPackageResources>?>? impacts )
    {
        _packages = packages;
        _localAggregates = localAggregates;
        _stableAggregates = stableAggregates;
        _stableIdentifiers = stableIdentifiers ?? [];
        _impacts = impacts ?? [];
    }

    void ICoreDataCache.LocalImplementationOnly() { }

    public void Write( ICKBinaryWriter w )
    {
        // AggregateKeys are not deserialized as AggregateKey but
        // only as the array of their PackageIndexes (the hash code is skipped).
        WriteAggregateKeys( w, _localAggregates );
        WriteAggregateKeys( w, _stableAggregates );

        w.WriteNonNegativeSmallInt32( _stableIdentifiers.Count );
        foreach( var id in _stableIdentifiers )
        {
            w.Write( id );
        }

        Throw.DebugAssert( "No <Code> and <App> entry.", _impacts.Count == 0 || _impacts.Count == _packages.Length - 1 );
        w.WriteNonNegativeSmallInt32( _impacts.Count );
        foreach( var revertResources in _impacts )
        {
            if( revertResources == null )
            {
                w.WriteSmallInt32( -1 );
            }
            else
            {
                w.WriteSmallInt32( revertResources.Count );
                foreach( var resource in revertResources )
                {
                    w.WriteNonNegativeSmallInt32( resource.Index );
                }
            }
        }

        static void WriteAggregateKeys( ICKBinaryWriter w, List<AggregateKey> aggregateKeys )
        {
            w.WriteNonNegativeSmallInt32( aggregateKeys.Count );
            foreach( var k in aggregateKeys )
            {
                var indexes = k.PackageIndexes;
                w.WriteNonNegativeSmallInt32( indexes.Length );
                foreach( var i in indexes )
                {
                    w.Write( i );
                }
            }
        }
    }

    public int StableAggregateCacheLength => _stableAggregates.Count;

    public int LocalAggregateCacheLength => _localAggregates.Count;

    public ImmutableArray<ResPackage> Packages => _packages;

    public IReadOnlyCollection<int> StableIdentifiers => _stableIdentifiers;

    public ReadOnlySpan<int> GetStableAggregate( int trueAggregateId )
    {
        Throw.DebugAssert( trueAggregateId >= 0 && trueAggregateId < _stableAggregates.Count );
        return _stableAggregates[trueAggregateId].PackageIndexes;
    }

    public ReadOnlySpan<int> GetLocalAggregate( int trueAggregateId )
    {
        Throw.DebugAssert( trueAggregateId >= 0 && trueAggregateId < _localAggregates.Count );
        return _localAggregates[trueAggregateId].PackageIndexes;
    }

    ReadOnlySpan<IResPackageResources> IInternalCoreDataCache.GetImpacts( ResPackage p )
    {
        // This is called only on the LiveSpaceDataCache.
        throw new NotSupportedException( "Never called." );
    }

}
