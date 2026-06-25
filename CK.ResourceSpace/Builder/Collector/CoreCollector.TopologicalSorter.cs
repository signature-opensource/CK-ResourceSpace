using CK.Engine.TypeCollector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CK.Core;

using Ref = ResPackageDescriptor.Ref;

sealed partial class CoreCollector
{
    sealed class TopologicalSorter
    {
        readonly IActivityMonitor _monitor;
        readonly CoreCollector _collector;
        // The sorter never alters the index. Only the CoreCollector does.
        readonly IReadOnlyDictionary<object, ResPackageDescriptor> _packageIndex;
        // Unfortunate duplicate index that memorizes resolved non-optional packages
        // as well as failure (null value) to avoid repeated calls to package resolver
        // (or ResPackageDescriptor.CheckContext).
        readonly Dictionary<object, object?> _resolved;

        public TopologicalSorter( IActivityMonitor monitor, CoreCollector collector )
        {
            _monitor = monitor;
            _collector = collector;
            _packageIndex = collector._packageIndex;
            _resolved = new Dictionary<object, object?>( _packageIndex.Count );
        }

        public bool Run()
        {
            if( InitialResolve() )
            {
                // Still optional packages can now be removed from the index.
                HandleOptionalPackages();
                // Second step: normalize the relationships to only have to deal with Requires and Children/Groups.
                //      - Transfer any RequiredBy to corresponding Requires
                //      - Transfer Package as a Children of its corresponding package.
                //      - Transfer Groups to corresponding Children.
                // This fails if a package.Package references a package.IsGroup.
                if( TransferToRequiresAndChildren() )
                {
                    // Third step: Projects the Children to their corresponding Groups.
                    // This fails if a Groups contains more than one package that is not a Group (but a Package).

                    // The initial package list and all requires and children are sorted by FullName to guaranty determinism.
                    // Reverting the names enables to discover missing topological constraints in the graph.
                    Comparison<ResPackageDescriptor> order = _collector._revertOrderingNames
                                                                ? ( x, y ) => y.FullName.CompareTo( x.FullName )
                                                                : ( x, y ) => x.FullName.CompareTo( y.FullName );
                    var context = new SortContext( order );
                    if( CleanChildrenAndTransferToGroups( ref context ) )
                    {
                        // The actual graph traversal can start.
                        _collector._packages.Sort( order );
                        return DoSort( ref context );
                    }
                }

            }
            return false;
        }

        void HandleOptionalPackages()
        {
            var l = _collector._optionalPackages;
            int totalCount = l.Count;
            if( totalCount > 0 )
            {
                int removedCount = 0;
                foreach( var p in l )
                {
                    if( p.IsOptional )
                    {
                        removedCount++;
                        _collector.RemoveDefinitelyOptional( p );
                    }
                }
                if( _monitor.ShouldLogLine( LogLevel.Info, null, out var tags ) )
                {
                    var b = new StringBuilder( "Optional packages:" ).AppendLine();
                    if( removedCount == 0 )
                    {
                        b.Append( "All " ).Append( totalCount )
                         .Append( " optional packages are no more optional:" ).AppendLine()
                         .AppendJoin( ", ", l.Select( p => p.FullName ) );
                    }
                    else if( removedCount == totalCount )
                    {
                        b.Append( "All " ).Append( removedCount )
                         .Append( " optional packages are eventually optional and will be ignored:" ).AppendLine()
                         .AppendJoin( ", ", l.Select( p => p.FullName ) );
                    }
                    else
                    {
                        b.Append( "Out of " ).Append( totalCount ).Append( " optional packages, " )
                         .Append( removedCount ).Append( " are eventually optional and will be ignored:" ).AppendLine()
                         .AppendJoin( ", ", l.Where( p => p.IsOptional ).Select( p => p.FullName ) ).AppendLine()
                         .Append( "Packages " ).AppendJoin( ", ", l.Where( p => !p.IsOptional ).Select( p => p.FullName ) )
                         .Append( " are no more optional." );
                    }
                    _monitor.UnfilteredLog( LogLevel.Info | LogLevel.IsFiltered, tags, b.ToString(), null );
                }
                if( removedCount > 0 )
                {
                    if( removedCount == totalCount )
                    {
                        l.Clear();
                    }
                    else
                    {
                        for( int i = 0; i < l.Count; i++ )
                        {
                            if( !l[i].IsOptional )
                            {
                                l.RemoveAt( i-- );
                            }
                        }
                    }
                }
            }
        }


        #region First step: initial resolution of references.
        bool InitialResolve()
        {
            bool success = true;
            var packages = _collector._packages;
            // Uses a for loop because the packages list can grow.
            for( int i = 0; i < packages.Count; i++ )
            {
                var p = packages[i];
                if( p.Package.IsValid )
                {
                    p.Package = InitialResolveRef( "Package", p.Package );
                    success &= p.Package.IsValid;
                }
                success &= InitialResolveReferences( "Requires", p._requires, autoRemove: p );
                success &= InitialResolveReferences( "RequiredBy", p._requiredBy, autoRemove: p );
                success &= InitialResolveReferences( "Groups", p._groups );
                success &= InitialResolveReferences( "Children", p._children );
            }
            return success;
        }

        bool InitialResolveReferences( string relName, List<Ref>? list, ResPackageDescriptor? autoRemove = null )
        {
            if( list == null ) return true;
            bool success = true;
            for( int i = 0; i < list.Count; i++ )
            {
                var r = list[i];
                // Silently skips an invalid reference that may have
                // been added to the list.
                if( r.IsValid )
                {
                    if( autoRemove != null && r.FullName == autoRemove.FullName )
                    {
                        list.RemoveAt( i-- );
                    }
                    else
                    {
                        r = InitialResolveRef( relName, r );
                        if( r.IsValid )
                        {
                            list[i] = r;
                        }
                        else
                        {
                            success = false;
                        }
                    }
                }
            }
            return success;
        }

        // The first "Resolve" pass calls InitialResolveRef on every references.
        // This can add new packages to the end of the _packages list of non
        // optional packages (the first pass uses a for, not a foreach).
        //
        // Optional references to optional packages are returned as-is.
        // Optional references to registered non-optional packages returns a non optional reference to the package.
        // Required reference to registered non-optional packages returns a non optional reference to the package.
        // Required reference to registered but optional packages promotes the package to be non-optional and returns
        // a non optional reference to the package.
        // Required reference to unexisting packages use the PackageResolver to return a non optional package
        // or fails by returning Ref.Invalid.
        //
        // A returned Ref.Invalid signals an error: the whole ResSpace is condemned. We use an awful forced null
        // package in the packageIndex to memorize the fact that resolution for this package fails and doesn't
        // need to be done again.
        //
        // This supports the "automatic closure discovery" feature. An extension of the initial set of types
        // is possible thanks to the PackageResolvers but under its control: when no resolver are configured,
        // the initial set of types is the definitive one.
        //
        // After the "Resolve" pass, the packages that are still optional can be removed and the second "Sort" pass
        // can start. During this step, optional references are resolved by the packageIndex or ignored.
        //
        Ref InitialResolveRef( string relName, Ref r )
        {
            Throw.DebugAssert( r.IsValid );
            if( _resolved.TryGetValue( r._ref, out var already ) )
            {
                if( already == null )
                {
                    return Ref.Invalid;
                }
                if( already is ResPackageDescriptor p )
                {
                    // If the cached package is still optional: returns the optional ref to it.
                    return new Ref( p, p.IsOptional );
                }
                Throw.DebugAssert( already is string or Type or ICachedType );
                // The cached result is the one of an optional reference that has not been resolved.
                if( r.IsOptional )
                {
                    // If the requested refernece is also optional: no change.
                    return r;
                }
                // This reference is not an optional one: continue with a real resolution.
            }
            var result = DoInitialResolveRef( relName, r );
            Throw.DebugAssert( "All Type are mapped to their ICachedType (or ResPackageDescriptor) from now on.",
                               (result.IsValid is false || r._ref is not Type) || result._ref is ICachedType or ResPackageDescriptor );
            Throw.DebugAssert( "IsOptional => IsValid. Invalid and Optional cannot exist.", !result.IsOptional || result.IsValid );
            Throw.DebugAssert( (result.IsValid && !result.IsOptional) == (result.AsPackageDescriptor?.IsOptional is false) );
            // Blindly remember the resulting referenced object.
            // This is a null for the Ref.Invalid, otherwise:
            // - this is necessarily a non optional package if the reference was not optional
            // - this can be a type, a string or a still optional package if the reference was optional.
            // Don't use Add on the dictionary here (we may come from an already cached optional reference and
            // having resolved a non optional one).
            _resolved[r.FullName] = result._ref;
            var t = r.AsType;
            if( t != null )
            {
                _resolved[t] = result._ref;
            }
            return result;

        }

        Ref DoInitialResolveRef( string relName, Ref r )
        {
            Throw.DebugAssert( r.IsValid );
            var p = r.AsPackageDescriptor;
            if( p != null )
            {
                // The reference has been directly configured to a package.
                // We need to ensure that the package is not optional (if the reference
                // is not) and check that there's no context mismatch.
                if( !r.IsOptional && p.IsOptional )
                {
                    if( !_collector.SetNoMoreOptionalPackage( _monitor, p ) )
                    {
                        return Ref.Invalid;
                    }
                }
                // On success, returns a non optional reference.
                return p.CheckContext( _monitor, relName, _collector._packageDescriptorContext )
                        ? p
                        : Ref.Invalid;
            }
            // Resolve Type to ICachedType.
            if( r._ref is Type rawType )
            {
                r = new Ref( _collector.TypeCache.Get( rawType ), r.IsOptional );
                Throw.DebugAssert( r.IsValid );
            }
            // Mapping from FullName or ICachedType to the package: this is were
            // SingleMapping is resolved.
            if( _packageIndex.TryGetValue( r._ref, out var result ) )
            {
                // The package is known. It is not optional, we are done
                // whether the reference is optional or not: the reference
                // is no more optional.
                if( !result.IsOptional )
                {
                    return result;
                }
                // The package is optional, if the ref is also optional,
                // we return the optional reference unchanged (but a Type is now a ICachedType).
                if( r.IsOptional )
                {
                    return r;
                }
                // This is a non optional ref to an optional package:
                // We promote the package: it is no more optional and
                // we return the resolved package.
                return _collector.SetNoMoreOptionalPackage( _monitor, result )
                        ? result
                        : Ref.Invalid;
            }
            // The package is unknown. If this is an optional
            // reference, we return the optional reference unchanged (but a Type is now a ICachedType).
            if( r.IsOptional )
            {
                return r;
            }
            // The package is unknown but this is a required reference.
            // => Time for the package resolver to kick in (if it exists).
            return InitialResolveWithResolvers( r );
        }

        Ref InitialResolveWithResolvers( Ref r )
        {
            Throw.DebugAssert( r.IsValid && !_packageIndex.ContainsKey( r._ref ) );
            if( _collector._packageResolver != null )
            {
                if( !_collector._packageResolver.ResolveRequired( _monitor, _collector, r )
                    || !_packageIndex.TryGetValue( r._ref, out var result ) )
                {
                    _monitor.Error( $"PackageResolver failed to register the reference '{r}'." );
                    return Ref.Invalid;
                }
                // Fix a stupid optional registration by the PackageResolver.
                return !result.IsOptional || _collector.SetNoMoreOptionalPackage( _monitor, result )
                        ? result
                        : Ref.Invalid;
            }
            _monitor.Error( $"No configured PackageResolver. The reference '{r}' is not registered." );
            return Ref.Invalid;
        }

        #endregion

        bool TransferToRequiresAndChildren()
        {
            Throw.DebugAssert( "There's no null in the cached reference because the first step succeeded.",
                               _resolved.Values.All( r => r != null ) );
            bool success = true;
            // Use foreach: this checks that during this step the package
            // list is not touched.
            foreach( var p in _collector._packages )
            {
                Throw.DebugAssert( "The packages list only contains non optional packages.", !p.IsOptional );
                if( p._requiredBy != null )
                {
                    foreach( var r in p._requiredBy )
                    {
                        var target = FinalResolve( r );
                        // Is is useless to find for an existing ref (probability to have
                        // A => B and B <= A is very low :-).
                        // Let's blindly duplicate a ref in this scenario.
                        target?.Requires.Add( p );
                    }
                }
                // Don't check Group vs. Package unicity constraint here.
                var targetPackage = FinalResolve( p.Package );
                if( targetPackage != null )
                {
                    if( targetPackage.IsGroup )
                    {
                        _monitor.Error( $"Package reference error from '{p}': the target '{targetPackage}' is Group, not a Package." );
                        success = false;
                    }
                    else
                    {
                        targetPackage.Children.Add( p );
                    }
                }
                if( p._groups != null )
                {
                    foreach( var r in p._groups )
                    {
                        var target = FinalResolve( r );
                        target?.Children.Add( p );
                    }
                }
            }
            return success;
        }

        bool CleanChildrenAndTransferToGroups( ref SortContext context )
        {
            bool success = true;
            // Use foreach: this checks that during this step the package
            // list is not touched.
            foreach( var p in _collector._packages )
            {
                // Skips when no children.
                if( p._children == null ) continue;
                // First, resolve the children to non optional ResPackageDescriptor,
                // remove duplicates and apply the name ordering to the children set.
                bool atLeastOne = false;
                foreach( var r in p._children )
                {
                    var child = FinalResolve( r );
                    if( child != null )
                    {
                        atLeastOne = true;
                        context.SharedBufferAddPackages( child );
                    }
                }
                if( !atLeastOne )
                {
                    // Normalizes to null.
                    p._children = null;
                    continue;
                }
                // Then, transfer children to groups while detecting "multiple package error" if
                // the current package is a package rather than a simple Group.
                foreach( var rChild in context.SharedBufferConclude( p._children ) )
                {
                    var child = Unsafe.As<ResPackageDescriptor>( rChild._ref! );
                    if( child._groups == null )
                    {
                        child.Groups.Add( p );
                    }
                    else
                    {
                        if( !p.IsGroup )
                        {
                            var alreadyPackage = child._groups.Select( r => r.AsPackageDescriptor )
                                                              .FirstOrDefault( IsActualPackage );
                            if( alreadyPackage != null && alreadyPackage != p )
                            {
                                _monitor.Error( $"Multiple package error: '{child}' is contained in '{p}' and '{alreadyPackage}' that are both Packages (and not Groups)." );
                                success = false;
                            }
                        }
                        child._groups.Add( p );
                    }
                }
            }
            return success;

            static bool IsActualPackage( ResPackageDescriptor? p ) => p != null && !p.IsOptional && !p.IsGroup;
        }

        enum Relationship
        {
            None,
            Requires,
            Contains
        }

        ref struct SortContext
        {
            public List<(ResPackageDescriptor P1,Relationship R, ResPackageDescriptor P2)>? CycleErrors;
            readonly HashSet<ResPackageDescriptor> _unsortedPackagesBuffer;
            readonly List<ResPackageDescriptor> _sortedPackagesBuffer;
            readonly Comparison<ResPackageDescriptor> _order;
            int _currentPackageIndex;
            int _currentResourceIndex;

            public SortContext( Comparison<ResPackageDescriptor> order )
            {
                _currentResourceIndex = -1;
                _currentPackageIndex = -1;
                _unsortedPackagesBuffer = new HashSet<ResPackageDescriptor>( 128 );
                _sortedPackagesBuffer = new List<ResPackageDescriptor>();
                _order = order;
            }

            internal bool InitializeError( ResPackageDescriptor p )
            {
                Throw.DebugAssert( CycleErrors == null );
                CycleErrors = [];
                return false;
            }

            internal readonly bool AddError( ResPackageDescriptor p1, Relationship relationship, ResPackageDescriptor p2 )
            {
                Throw.DebugAssert( CycleErrors != null );
                Throw.DebugAssert( relationship is not Relationship.None );
                CycleErrors.Add( (p1,relationship,p2) );
                return false;
            }

            internal readonly string BuildCyclicError()
            {
                Throw.DebugAssert( CycleErrors != null );
                var b = new StringBuilder();
                bool atLeastOne = false;
                foreach( var e in CycleErrors )
                {
                    if( atLeastOne ) b.Append( ", " );
                    atLeastOne = true;
                    b.Append( '\'' ).Append( e.P1.FullName ).Append( '\'' )
                     .Append( e.R switch { Relationship.Requires => " requires ", _ => " contains " } )
                     .Append( '\'' ).Append( e.P2.FullName ).Append( '\'' );
                }
                return b.ToString();
            }

            internal int GetNextPackageIndex() => ++_currentPackageIndex;

            internal int GetNextResourceIndex() => ++_currentResourceIndex;

            internal void UpdateNextResourceIndex( int idxHeaderOrFooter )
            {
                if( idxHeaderOrFooter > _currentResourceIndex )
                {
                    _currentResourceIndex = idxHeaderOrFooter;
                }
            }

            internal readonly void SharedBufferAddPackages( ResPackageDescriptor d )
            {
                Throw.DebugAssert( !d.IsOptional );
                _unsortedPackagesBuffer.Add( d );
            }

            internal readonly List<Ref> SharedBufferConclude( List<Ref> finalRefs )
            {
                _sortedPackagesBuffer.AddRange( _unsortedPackagesBuffer );
                _unsortedPackagesBuffer.Clear();
                // Sort and update the Refs.
                var s = CollectionsMarshal.AsSpan( _sortedPackagesBuffer );
                s.Sort( _order );
                finalRefs.Clear();
                foreach( var d in s )
                {
                    finalRefs.Add( d );
                }
                _sortedPackagesBuffer.Clear();
                return finalRefs;
            }
        }

        bool DoSort( ref SortContext context )
        {
            // Use foreach: this checks that during this step the package
            // list is not touched.
            foreach( var p in _collector._packages )
            {
                if( !HandlePackageHeader( ref context, p )
                    || !HandlePackageFooter( ref context, p ) )
                {
                    break;
                }
            }
            if( context.CycleErrors != null )
            {
                _monitor.Error( $"""
                    Cyclic dependency error:
                    {context.BuildCyclicError()}
                    """ );
                return false;
            }
            // Sorts the _packages list in place according to the computed _idxPackage.
            // No need to allocate a new array for this.
            _collector._packages.Sort( (x,y) => x._idxPackage - y._idxPackage );
            return true;
        }

        bool HandlePackageHeader( ref SortContext context, ResPackageDescriptor p )
        {
            if( p._idxHeader >= 0 )
            {
                return true;
            }
            if( p._idxHeader == -2 )
            {
                return context.InitializeError( p );
            }
            p._idxHeader = -2;

            // Too verbose.
            // using var _ = _monitor.OpenDebug( $"Header {p}" ).ConcludeWith( () => $"Header {p} -> {p._idxHeader}" );

            // No need to cleanup the groups list here: it's no more used.
            if( p._groups != null )
            {
                // This is where the sorter is not perfectly determinist. If [Groups] are used (but this is
                // barely used), because we don't sort the groups here, final order may be impacted (the result
                // will always be correct but not exactly the same for the same logical graph).
                // To make the sorter fully determinist, groups should be ordered here.
                foreach( var g in p._groups )
                {
                    var group = g.AsPackageDescriptor;
                    if( group != null && !group.IsOptional )
                    {
                        if( !HandlePackageHeader( ref context, group ) )
                        {
                            return context.AddError( group, Relationship.Contains, p );
                        }
                    }
                }
            }
            bool hasActualRequires = false;
            if( p._requires != null )
            {
                for( int i = 0; i < p._requires.Count; i++ )
                {
                    Ref r = p._requires[i];
                    var target = FinalResolve( r );
                    if( target != null )
                    {
                        context.SharedBufferAddPackages( target );
                        hasActualRequires = true;
                    }
                    else
                    {
                        p._requires.RemoveAt( i-- );
                    }
                }
                if( hasActualRequires )
                {
                    foreach( var target in context.SharedBufferConclude( p._requires ) )
                    {
                        if( !HandleRequires( ref context, p, Unsafe.As<ResPackageDescriptor>( target._ref! ) ) )
                        {
                            return false;
                        }
                    }
                }
            }
            Throw.DebugAssert( !hasActualRequires || p._idxHeader >= 0 );
            if( !hasActualRequires )
            {
                // This package requires nothing. It is an exit point,
                // its ResPackage will require the <Code>.
                // We normalize the now empty list to null.
                // The builder will use this to detect the exit point case.
                Throw.DebugAssert( p._requires == null || p._requires.Count == 0 );
                p._requires = null;
            }
            p._idxHeader = context.GetNextResourceIndex();
            return true;
        }

        bool HandleRequires( ref SortContext context, ResPackageDescriptor p, ResPackageDescriptor target )
        {
            if( !HandlePackageFooter( ref context, target ) )
            {
                return context.AddError( p, Relationship.Requires, target );
            }
            target._hasIncomingDeps = true;
            p._idxHeader = Math.Max( p._idxHeader, target._idxFooter + 1 );
            return true;
        }

        bool HandlePackageFooter( ref SortContext context, ResPackageDescriptor p )
        {
            if( p._idxFooter >= 0 )
            {
                return true;
            }
            if( p._idxFooter == -2 )
            {
                return context.InitializeError( p );
            }
            Throw.DebugAssert( p._idxFooter == -1 );

            // Too verbose.
            // using var _ = _monitor.OpenDebug( $"Footer {p}" ).ConcludeWith( () => $"Footer {p} -> {p._idxFooter}" );

            p._idxFooter = -2;
            if( p._children != null )
            {
                Throw.DebugAssert( p._children.Count > 0 );
                Throw.DebugAssert( p._children.All( r => r.AsPackageDescriptor != null && r.AsPackageDescriptor == FinalResolve( r ) ) );
                foreach( var r in p._children )
                {
                    var child = r.AsPackageDescriptor!;
                    if( !HandleChild( ref context, p, child ) )
                    {
                        return false;
                    }
                }
            }
            else
            {
                // Since there is no children, no one handled the header.
                if( !HandlePackageHeader( ref context, p ) )
                {
                    return false;
                }
            }
            p._idxFooter = context.GetNextResourceIndex();
            p._idxPackage = context.GetNextPackageIndex();
            return true;
        }

        bool HandleChild( ref SortContext context, ResPackageDescriptor p, ResPackageDescriptor child )
        {
            if( !HandlePackageFooter( ref context, child ) )
            {
                return context.AddError( p, Relationship.Contains, child );
            }
            child._hasIncomingDeps = true;
            p._idxFooter = Math.Max( p._idxFooter, child._idxFooter + 1 );
            return true;
        }

        ResPackageDescriptor? FinalResolve( Ref r )
        {
            if( r.IsValid )
            {
                Throw.DebugAssert( r.IsOptional || r.AsPackageDescriptor?.IsOptional is false );
                if( !r.IsOptional )
                {
                    return r.AsPackageDescriptor;
                }
                // The reference is optional. It may now be resolvable.
                if( _resolved.TryGetValue( r._ref, out var resolved )
                    && resolved is ResPackageDescriptor p
                    && !p.IsOptional )
                {
                    return p;
                }
            }
            return null;
        }
    }
}
