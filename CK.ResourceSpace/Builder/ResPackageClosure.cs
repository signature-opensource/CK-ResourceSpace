using CK.Engine.TypeCollector;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Core;

using Ref = ResPackageDescriptor.Ref;

/// <summary>
/// Computes the packages that a <see cref="ResSpace"/> keeps from a set of candidate typed packages, before any
/// <see cref="ResPackageDescriptor"/> is created: only the types (their attributes and their mappings, see
/// <see cref="ICachedType.TryGetMapping(IActivityMonitor, out CachedTypeMapping?)"/>) are read.
/// <para>
/// When the candidates are registered with the roots as required and the other ones as optional, the ResSpace keeps
/// exactly this closure (provided that no relation and no name mapping is added to the descriptors by code):
/// <list type="bullet">
///     <item>The roots are kept.</item>
///     <item>
///     The required references of a kept package (its <see cref="ResPackageDescriptor.Package"/>, Requires, RequiredBy,
///     Children, Groups and its <see cref="ICachedType.AlsoRegisterTypes"/>) are kept. This is "forward only": a package
///     that declares a RequiredBy or a Groups to a kept package is not kept because of it.
///     </item>
///     <item>The optional references ([Optional...] variants) never keep a package.</item>
///     <item>A reference to a multiple abstraction never keeps a package.</item>
///     <item>
///     A required reference to a single abstraction keeps its implementation: the only root candidate that implements it,
///     otherwise the only candidate that implements it.
///     </item>
///     <item>A required reference that cannot be resolved among the candidates is an error.</item>
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
    /// <returns>The kept packages or null on error.</returns>
    public static HashSet<ICachedType>? Compute( IActivityMonitor monitor,
                                                 GlobalTypeCache typeCache,
                                                 IEnumerable<ICachedType> candidates,
                                                 Func<ICachedType, bool> isRoot )
    {
        Throw.CheckNotNullArgument( monitor );
        Throw.CheckNotNullArgument( typeCache );
        Throw.CheckNotNullArgument( candidates );
        Throw.CheckNotNullArgument( isRoot );

        var state = new State( monitor, typeCache );
        var resourceGroup = typeof( IResourceGroup );
        foreach( var c in candidates )
        {
            if( !c.TryGetMapping( monitor, out var mapping ) )
            {
                state.Success = false;
                continue;
            }
            if( mapping.IsKey )
            {
                monitor.Error( $"Type '{c.CSharpName}' is an abstraction (it has a {mapping.Cardinality} mapping): it cannot be a package." );
                state.Success = false;
            }
            else if( !state.Index.TryAdd( c, c ) )
            {
                monitor.Error( $"Duplicate candidate package '{c.CSharpName}'." );
                state.Success = false;
            }
            else if( !state.Index.TryAdd( c.Type.FullName!, c ) )
            {
                monitor.Error( $"Candidate package '{c.CSharpName}' has the same full name as '{state.Index[c.Type.FullName!].CSharpName}'." );
                state.Success = false;
            }
            else
            {
                bool root = isRoot( c );
                if( root ) state.Roots.Add( c );
                foreach( var k in mapping.SingleKeys )
                {
                    if( !resourceGroup.IsAssignableFrom( k.Type ) ) continue;
                    if( !state.SingleImplementations.TryGetValue( k, out var list ) )
                    {
                        list = new List<ICachedType>();
                        state.SingleImplementations.Add( k, list );
                    }
                    list.Add( c );
                }
                if( root && state.Result.Add( c ) )
                {
                    state.Queue.Enqueue( c );
                }
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
        // A single abstraction cannot have more than one kept implementation.
        foreach( var (key, implementations) in state.SingleImplementations )
        {
            if( implementations.Count( state.Result.Contains ) > 1 )
            {
                monitor.Error( $"'{key.CSharpName}' is a single abstraction but more than one kept package implement it: '{implementations.Where( state.Result.Contains ).Select( i => i.CSharpName ).Order().Concatenate( "', '" )}'." );
                state.Success = false;
            }
        }
        return state.Success ? state.Result : null;
    }

    sealed class State
    {
        readonly IActivityMonitor _monitor;
        readonly GlobalTypeCache _typeCache;
        public readonly Dictionary<object, ICachedType> Index = new();
        public readonly HashSet<ICachedType> Roots = new();
        public readonly Dictionary<ICachedType, List<ICachedType>> SingleImplementations = new();
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
            if( !Index.TryGetValue( key, out var target ) )
            {
                if( key is ICachedType abstraction
                    && abstraction.TryGetMapping( _monitor, out var mapping )
                    && mapping.IsKey
                    && typeof( IResourceGroup ).IsAssignableFrom( abstraction.Type ) )
                {
                    if( mapping.Cardinality == MappingCardinality.Multiple )
                    {
                        if( relName == "Package" )
                        {
                            _monitor.Error( $"The package of '{Current.CSharpName}' cannot be '{abstraction.CSharpName}' that is a multiple abstraction." );
                            Success = false;
                        }
                        // A reference to a multiple abstraction never keeps a package.
                        return;
                    }
                    target = ResolveSingle( abstraction );
                    if( target == null )
                    {
                        Success = false;
                        return;
                    }
                }
                else
                {
                    _monitor.Error( $"'{Current.CSharpName}' references '{r.FullName}' ({relName}) that is not a candidate package." );
                    Success = false;
                    return;
                }
            }
            if( Result.Add( target ) ) Queue.Enqueue( target );
        }

        ICachedType? ResolveSingle( ICachedType key )
        {
            SingleImplementations.TryGetValue( key, out var implementations );
            if( implementations != null )
            {
                var roots = implementations.Where( Roots.Contains ).ToList();
                if( roots.Count == 1 ) return roots[0];
                if( roots.Count == 0 && implementations.Count == 1 ) return implementations[0];
                _monitor.Error( $"""
                    Ambiguous single abstraction '{key.CSharpName}' referenced by '{Current!.CSharpName}': it is implemented by '{implementations.Select( i => i.CSharpName ).Order().Concatenate( "', '" )}' and {(roots.Count == 0 ? "none of them is" : $"{roots.Count} of them are")} a root.
                    Exactly one implementation must be a root, or be the only candidate.
                    """ );
            }
            else
            {
                _monitor.Error( $"No candidate package implements '{key.CSharpName}' (a single abstraction) referenced by '{Current!.CSharpName}'." );
            }
            return null;
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
