using CK.Engine.TypeCollector;
using System;
using System.Collections.Generic;

namespace CK.Core;

using Ref = ResPackageDescriptor.Ref;

/// <summary>
/// Computes the packages that a <see cref="ResSpace"/> keeps from a set of candidate typed packages, before any
/// <see cref="ResPackageDescriptor"/> is created: only the types' attributes are read.
/// <para>
/// When the candidates are registered with the roots as required and the other ones as optional, the ResSpace keeps
/// exactly this closure (provided that the single mappings are the same and that no relation is added to the descriptors):
/// <list type="bullet">
///     <item>The roots are kept.</item>
///     <item>
///     The required references of a kept package (its <see cref="ResPackageDescriptor.Package"/>, Requires, RequiredBy,
///     Children, Groups and its <see cref="ICachedType.AlsoRegisterTypes"/>) are kept. This is "forward only": a package
///     that declares a RequiredBy or a Groups to a kept package is not kept because of it.
///     </item>
///     <item>The optional references ([Optional...] variants) never keep a package.</item>
///     <item>A required reference to a package that is not a candidate is an error.</item>
/// </list>
/// </para>
/// </summary>
public static class ResPackageClosure
{
    /// <summary>
    /// Computes the closure.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="typeCache">The type cache.</param>
    /// <param name="candidates">The candidate typed packages.</param>
    /// <param name="isRoot">Whether a candidate is a root (registered as required).</param>
    /// <param name="singleMappings">
    /// Optional aliases (a <see cref="string"/> or a <see cref="ICachedType"/>) of candidate packages, like the
    /// ones added by <see cref="ResPackageDescriptor.AddSingleMapping(IActivityMonitor, ICachedType)"/>.
    /// </param>
    /// <returns>The kept packages or null on error.</returns>
    public static HashSet<ICachedType>? Compute( IActivityMonitor monitor,
                                                 GlobalTypeCache typeCache,
                                                 IEnumerable<ICachedType> candidates,
                                                 Func<ICachedType, bool> isRoot,
                                                 IReadOnlyDictionary<object, ICachedType>? singleMappings = null )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( typeCache );
        Throw.CheckNotNullArgument( candidates );
        Throw.CheckNotNullArgument( isRoot );

        var state = new State( monitor, typeCache );
        foreach( var c in candidates )
        {
            if( !state.Index.TryAdd( c, c ) )
            {
                monitor.Error( $"Duplicate candidate package '{c.CSharpName}'." );
                state.Success = false;
            }
            else if( !state.Index.TryAdd( c.Type.FullName!, c ) )
            {
                monitor.Error( $"Candidate package '{c.CSharpName}' has the same full name as '{state.Index[c.Type.FullName!].CSharpName}'." );
                state.Success = false;
            }
            else if( isRoot( c ) && state.Result.Add( c ) )
            {
                state.Queue.Enqueue( c );
            }
        }
        if( singleMappings != null )
        {
            foreach( var (alias, target) in singleMappings )
            {
                state.Success &= AddMapping( monitor, state.Index, alias, target );
            }
        }
        if( !state.Success ) return null;

        while( state.Queue.TryDequeue( out var t ) )
        {
            state.Current = t;
            var visitor = new Follower( state );
            // Don't use &=: the visitor sets state.Success during the read.
            if( !ResPackageRelationReader.Read( monitor, t, ref visitor ) ) state.Success = false;
            foreach( var also in t.AlsoRegisterTypes )
            {
                state.Follow( "AlsoRegisterType", new Ref( also ) );
            }
        }
        return state.Success ? state.Result : null;

        static bool AddMapping( IActivityMonitor monitor, Dictionary<object, ICachedType> index, object alias, ICachedType target )
        {
            if( !index.TryGetValue( target, out _ ) )
            {
                monitor.Error( $"Alias '{alias}' is mapped to '{target.CSharpName}' that is not a candidate package." );
                return false;
            }
            if( alias is ICachedType tAlias )
            {
                if( !tAlias.Type.IsAssignableFrom( target.Type ) )
                {
                    monitor.Error( $"Type '{tAlias.CSharpName}' cannot be mapped to '{target.CSharpName}' because it is not assignable from the package's Type." );
                    return false;
                }
            }
            else if( alias is not string s || string.IsNullOrWhiteSpace( s ) )
            {
                Throw.ArgumentException( nameof( singleMappings ), $"Alias must be a non empty string or a ICachedType: '{alias}' is invalid." );
            }
            if( !index.TryAdd( alias, target ) )
            {
                monitor.Error( $"Alias '{alias}' cannot be mapped to '{target.CSharpName}' as it is already mapped to '{index[alias].CSharpName}'." );
                return false;
            }
            return true;
        }
    }

    sealed class State
    {
        readonly IActivityMonitor _monitor;
        readonly GlobalTypeCache _typeCache;
        public readonly Dictionary<object, ICachedType> Index = new();
        public readonly HashSet<ICachedType> Result = new();
        public readonly Queue<ICachedType> Queue = new();
        public ICachedType? Current;
        public bool Success = true;

        public State( IActivityMonitor monitor, GlobalTypeCache typeCache )
        {
            _monitor = monitor;
            _typeCache = typeCache;
        }

        public void Follow( string relName, Ref r )
        {
            Throw.DebugAssert( r.IsValid && !r.IsOptional && Current != null );
            object key = r._ref is Type t ? _typeCache.Get( t ) : r._ref;
            if( Index.TryGetValue( key, out var target ) )
            {
                if( Result.Add( target ) ) Queue.Enqueue( target );
            }
            else
            {
                _monitor.Error( $"'{Current.CSharpName}' references '{r.FullName}' ({relName}) that is not a candidate package." );
                Success = false;
            }
        }
    }

    readonly struct Follower : IResPackageRelationVisitor
    {
        readonly State _state;

        public Follower( State state ) => _state = state;

        public void OnPackage( Ref package ) => _state.Follow( "Package", package );

        public void OnRelation( ResRelationKind kind, Ref target )
        {
            if( !target.IsOptional ) _state.Follow( kind.ToString(), target );
        }
    }
}
