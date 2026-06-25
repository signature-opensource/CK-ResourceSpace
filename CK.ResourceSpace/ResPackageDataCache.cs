using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CK.Core;

/// <summary>
/// Template method pattern that manage cached information associated a <see cref="ResPackage"/>.
/// </summary>
/// <typeparam name="T">
/// Associated data type. It should be immutable and is fully "implemented" by the
/// <see cref="Create(IActivityMonitor, ResPackage)"/>, <see cref="Combine(IActivityMonitor, IResPackageResources, T)"/>
/// and <see cref="Aggregate(T, T)"/> methods.
/// </typeparam>
public abstract class ResPackageDataCache<T> where T : class
{
    readonly IInternalCoreDataCache _cache;
    readonly T?[] _data;
    readonly T?[] _stableAggregateCache;
    readonly T?[] _localAggregateCache;

    /// <summary>
    /// Initializes a new empty ResPackageDataCache.
    /// </summary>
    /// <param name="cache">The required cache provided by the <see cref="ResCoreData.SpaceDataCache"/>.</param>
    protected ResPackageDataCache( ICoreDataCache cache )
    {
        var c = (IInternalCoreDataCache)cache;
        _cache = c;
        _data = new T[c.Packages.Length];
        _stableAggregateCache = new T[c.StableAggregateCacheLength];
        _localAggregateCache = new T[c.LocalAggregateCacheLength];
    }

    /// <summary>
    /// Gets the cache instance to which this data handler is bound.
    /// </summary>
    public ICoreDataCache SpaceCache => _cache;

    /// <summary>
    /// Gets the data associated to a <see cref="ResPackage"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="package">The package for wich a <typeparamref name="T"/> must be obtained.</param>
    /// <returns>The data on success, null on error.</returns>
    public T? Obtain( IActivityMonitor monitor, ResPackage package )
    {
        var d = _data[package.Index];
        if( d == null )
        {
            d = Build( monitor, package );
            _data[package.Index] = d;
        }
        return d;
    }

    T? Build( IActivityMonitor monitor, ResPackage package )
    {
        T? result = null;
        var (requiresAggregateId, childrenAggregateId) = package.GetAggregateIdentifiers();
        if( requiresAggregateId == default )
        {
            result = Create( monitor, package );
        }
        else
        {
            var before = GetAggregate( monitor, requiresAggregateId );
            if( before != null )
            {
                result = Combine( monitor, package.Resources, before );
            }
        }
        if( result != null && package.Children.Length > 0 )
        {
            var content = GetAggregate( monitor, childrenAggregateId );
            result = content != null
                        ? Aggregate( result, content )
                        : null;
        }
        if( result != null )
        {
            result = Combine( monitor, package.AfterResources, result );
        }
        return result;
    }

    T? GetAggregate( IActivityMonitor monitor, AggregateId id )
    {
        if( !TryGetAggregate( monitor, _stableAggregateCache, id._stableKeyId, out var stable ) 
            || !TryGetAggregate( monitor, _localAggregateCache, id._localKeyId, out var local ) )
        {
            return null;
        }
        return stable == null
                ? local
                : local == null
                    ? stable
                    : Aggregate( local, stable );
                    
    }

    T? Obtain( IActivityMonitor monitor, int packageIndex )
    {
        var d = _data[packageIndex];
        if( d == null )
        {
            d = Build( monitor, _cache.Packages[packageIndex] );
            _data[packageIndex] = d;
        }
        return d;
    }

    bool TryGetAggregate( IActivityMonitor monitor, T?[] aggregateCache, int keyId, out T? data )
    {
        if( keyId == 0 )
        {
            data = null;
            return true;
        }
        // Offsets keyId by 1.
        int trueAggId = --keyId - _data.Length;
        if( trueAggId < 0 )
        {
            // Single package: use Obtain from package index.
            data = Obtain( monitor, keyId );
            return data != null;
        }
        // Aggregate: it's already available or we build it.
        data = aggregateCache[trueAggId];
        if( data == null )
        {
            var packageIdentifiers = aggregateCache == _stableAggregateCache
                                        ? _cache.GetStableAggregate( trueAggId )
                                        : _cache.GetLocalAggregate( trueAggId );
            Throw.DebugAssert( packageIdentifiers.Length >= 2 );
            // Building the aggregate from its packages.
            var e = packageIdentifiers.GetEnumerator();
            e.MoveNext();
            data = Obtain( monitor, e.Current );
            if( data == null ) return false;
            while( e.MoveNext() )
            {
                var next = Obtain( monitor, e.Current );
                if( next == null ) return false;
                data = Aggregate( data, next );
            }
            aggregateCache[trueAggId] = data;
        }
        return true;
    }

    /// <summary>
    /// Initializer function called for packages that have no <see cref="ResPackage.Reachables"/>.
    /// Returning null indicates a failure, errors must have been logged.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="package">The package without dependency for which a <typeparamref name="T"/> must be created.</param>
    /// <returns>The associated data or null on error.</returns>
    protected abstract T? Create( IActivityMonitor monitor, ResPackage package );

    /// <summary>
    /// Applies whatever contains a <see cref="IResPackageResources"/> to an existing data.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="resources">The package's resources to consider.</param>
    /// <param name="data">The data initial data to consider.</param>
    /// <returns>The associated data or null on error.</returns>
    protected abstract T? Combine( IActivityMonitor monitor, IResPackageResources resources, T data );

    /// <summary>
    /// Aggregates two associated data into one. This cannot fail: if the aggregation is somehow invalid,
    /// this must appear in the <typeparamref name="T"/>.
    /// </summary>
    /// <param name="data1">The first data to aggregate.</param>
    /// <param name="data2">The second data to aggregate.</param>
    /// <returns>Aggregated data. Can be <paramref name="data1"/> or <paramref name="data2"/>.</returns>
    protected abstract T Aggregate( T data1, T data2 );

    /// <summary>
    /// Gets the stable data that can be saved to restore a cache with the stable data.
    /// </summary>
    /// <returns>The stable data to persist.</returns>
    public ImmutableArray<T> GetStableData()
    {
        var stableIdentifiers = _cache.StableIdentifiers;
        var result = new T[stableIdentifiers.Count];
        int i = 0;
        foreach( var stableId in stableIdentifiers )
        {
            T? t;
            // Offsets id by 1.
            int id = stableId - 1;
            int trueAggId = id - _data.Length;
            if( trueAggId < 0 )
            {
                t = _data[id];
            }
            else
            {
                t = _stableAggregateCache[trueAggId];
            }
            Throw.CheckState("Cache must have been fully initialized before calling GetStableData.", t != null );
            result[i] = t;
            ++i;
        }
        return ImmutableCollectionsMarshal.AsImmutableArray( result );
    }

    /// <summary>
    /// Sets stable data.
    /// </summary>
    /// <param name="stableData">
    /// Stable data obtained by <see cref="GetStableData()"/>. Except the size of the array, no checks are done:
    /// the data must correspond to the same <see cref="ICoreDataCache"/> (or to a deserialized instance).
    /// </param>
    public void SetStableData( ImmutableArray<T> stableData )
    {
        Throw.CheckArgument( stableData.Length == _cache.StableIdentifiers.Count );
        var stableIdentifiers = _cache.StableIdentifiers;
        int i = 0;
        foreach( var stableId in stableIdentifiers )
        {
            // Offsets id by 1.
            int id = stableId - 1;
            int trueAggId = id - _data.Length;
            if( trueAggId < 0 )
            {
                _data[id] = stableData[i];
            }
            else
            {
                _stableAggregateCache[trueAggId] = stableData[i];
            }
            ++i;
        }
    }

    /// <summary>
    /// Invalidates the cache from the <paramref name="resources"/> (that must be local: <see cref="IResPackageResources.LocalPath"/>
    /// is not null) up to the "&lt;App&gt;" tail package.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="resources">The local resources.</param>
    public void InvalidateCache( IActivityMonitor monitor, IResPackageResources resources )
    {
        Throw.CheckArgument( "The resources must be local.", resources.LocalPath != null );
        var p = resources.Package;
        // Our Resources or AfterResources have changed: our associated data
        // is dirty but the data associated to our aggregates have not changed.
        _data[p.Index] = null;
        // We propagate the change of this package to its impacts.
        foreach( var i in _cache.GetImpacts( p ) )
        {
            Invalidate( monitor, i );
        }
    }

    void Invalidate( IActivityMonitor monitor, IResPackageResources i )
    {
        var (requiresAggregateId, childrenAggregateId) = i.Package.GetAggregateIdentifiers();
        if( !i.IsAfter )
        {
            // A requires changed: our requiresAggregateId is dirty.
            Throw.DebugAssert( requiresAggregateId.HasLocal );
            InvalidateAggregate( requiresAggregateId );
        }
        else
        {
            // A children changed: our childrenAggregateId is dirty.
            Throw.DebugAssert( childrenAggregateId.HasLocal );
            InvalidateAggregate( childrenAggregateId );
        }
        // Our cached data is dirty.
        _data[i.Package.Index] = null;
        // We recursively propagate the change of this package to its impacts.
        foreach( var r in _cache.GetImpacts( i.Package ) )
        {
            Invalidate( monitor, r );
        }
    }

    void InvalidateAggregate( AggregateId aggregateId )
    {
        Throw.DebugAssert( aggregateId.HasLocal );
        // Offset by 1.
        int id = aggregateId._localKeyId - 1;
        int trueAggId = id - _data.Length;
        if( trueAggId >= 0 )
        {
            _localAggregateCache[trueAggId] = null;
        }
    }
}
