using CK.EmbeddedResources;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CK.Core;

/// <summary>
/// Handles a <see cref="ResSpaceCollector"/>: topologically sorts its configured <see cref="ResPackageDescriptor"/>
/// to produce a <see cref="ResSpaceData"/> with its final <see cref="ResPackage"/>.
/// </summary>
public sealed class ResSpaceDataBuilder
{
    readonly ResSpaceCollector _collector;
    IResourceContainer? _generatedCodeContainer;

    /// <summary>
    /// Initializes a new builder.
    /// </summary>
    /// <param name="collector">The package collector.</param>
    public ResSpaceDataBuilder( ResSpaceCollector collector )
    {
        _collector = collector;
        _generatedCodeContainer = collector.GeneratedCodeContainer;
    }

    /// <summary>
    /// Gets or sets the configured Code generated resource container.
    /// This can only be set if this has not been previously set (ie. this is null).
    /// See <see cref="ResSpaceConfiguration.GeneratedCodeContainer"/>.
    /// </summary>
    [DisallowNull]
    public IResourceContainer? GeneratedCodeContainer
    {
        get => _generatedCodeContainer;
        set
        {
            Throw.CheckNotNullArgument( value );
            Throw.CheckState( "This can be set only once.", GeneratedCodeContainer is null );
            _generatedCodeContainer = value;
        }
    }

    /// <summary>
    /// Produces the <see cref="ResSpaceData"/> with its final <see cref="ResPackage"/>
    /// toplogically sorted. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The space data on success, null otherwise.</returns>
    public ResSpaceData? Build( IActivityMonitor monitor )
    {
        if( !_collector.CloseRegistrations( monitor, out var codeHandledResources, out var finalOptionalPackages ) )
        {
            return null;
        }

        // The "<Code>" package is the first package and represents the generated code.
        // It is empty (no child) and only contains the generated code as AfterResources by design.
        // The code container, if not yet knwon, is a ResourceContainerWrapper that can transition from
        // an empty default container to the real code container when set.
        // This package is not local as it is not bound to any local path.
        // All packages that require no other package require it.
        // Note: We could choose the BeforeResources to hold the code generated container, this wouldn't
        //       change anything but this choice is settled now.
        static ResPackage CreateCodePackage( ResCoreDataCacheBuilder dataCacheBuilder, IResourceContainer? generatedCodeContainer )
        {
            var noHeadRes = new EmptyResourceContainer( "<Code>", isDisabled: true );
            var codeContainer = generatedCodeContainer
                                ?? new ResourceContainerWrapper( new EmptyResourceContainer( "Empty <Code>", isDisabled: false ) );
            // Code package has no content and is by construction the first package: its index and
            // the indexes of its resources are known.
            return new ResPackage( dataCacheBuilder,
                                   "<Code>",
                                   defaultTargetPath: default,
                                   idxBeforeResources: 0,
                                   beforeResources: noHeadRes,
                                   idxAfterResources: 1,
                                   afterResources: codeContainer,
                                   isGroup: false,
                                   type: null,
                                   requires: ImmutableArray<ResPackage>.Empty,
                                   children: ImmutableArray<ResPackage>.Empty,
                                   index: 0 );
        }

        // The "<App>" package is the last package and represents the Application being setup.
        // It is empty (no child) and only contains BeforeResources with a FileSystemResourceContainer
        // on the AppResourcesLocalPath and an empty container disabled by design AfterResources.
        // Note: We could choose the AfterResources to hold the app resources, this wouldn't
        //       change anything but this choice is settled now, it mirrors the <Code> resources.
        //       The allPackageResources is:
        //         - Before Code => Empty by design.
        //         - After Code => CodeGenerated.
        //         - ... all other package's resources.
        //         - Before App => App resource folder.
        //         - After App => Empty by design.
        //
        // It is a local package by design except if AppResourcesLocalPath is null.
        // It requires all packages that are not required by other packages.
        //
        // It is initialized below after the others (once we know its requirements) and the indexes.
        static ResPackage CreateAppPackage( ref string? appLocalPath,
                                            ResCoreDataCacheBuilder dataCacheBuilder,
                                            ImmutableArray<ResPackage> appRequires,
                                            int index )
        {
            IResourceContainer appResStore;
            if( appLocalPath != null )
            {
                appResStore = new FileSystemResourceContainer( appLocalPath, "<App>" );
                // Use the path normalization.
                appLocalPath = appResStore.ResourcePrefix;
            }
            else
            {
                appResStore = new EmptyResourceContainer( "Empty <Code>", isDisabled: false );
            }
            var noAppRes = new EmptyResourceContainer( "<App>", isDisabled: true );
            int idxResources = 2 * index; 
            var appPackage = new ResPackage( dataCacheBuilder,
                                             "<App>",
                                             defaultTargetPath: default,
                                             idxBeforeResources: idxResources,
                                             beforeResources: appResStore,
                                             idxAfterResources: idxResources + 1,
                                             afterResources: noAppRes,
                                             isGroup: false,
                                             type: null,
                                             requires: appRequires,
                                             children: ImmutableArray<ResPackage>.Empty,
                                             index );
            return appPackage;
        }

        var descriptorPackageCount = _collector.Packages.Count;
        string? appLocalPath = _collector.AppResourcesLocalPath;
        var localCount = _collector.LocalPackageCount;

        // We have <Code> + descriptorPackageCount + <App> total packages.
        var bAll = ImmutableArray.CreateBuilder<ResPackage>( descriptorPackageCount + 2 );
        // We have 2 * (<Code> + descriptorPackageCount + <App>) total IResPackageResources (the After and Before).
        // We don't use a list/builder here because indexes are provided by the SortedItems.
        // IResPackageResources.Index is zero based.
        var allPackageResources = new IResPackageResources[ 2 * (descriptorPackageCount+2) ];
        // The final size of the package index:
        // - 1 FullName per ResPackageDescriptor.
        // - Plus the type for the typed package.
        // - Plus the number of single mappings.
        // - Plus the name of the "<Code>" and "<App>" packages.
        var packageIndexSize = descriptorPackageCount + _collector.SingleMappingCount + _collector.TypedPackageCount + 2;
        // The final size of the _resourceIndex (the IResourceContainer to IResPackageResources map
        // for BeforeResources and AfterResources plus the GeneratedCodeContainer or the wrapper).
        // This is by default. AppResourcesLocalPath can add 1 more key.
        // Note:
        //   The local IResPackageResources[] size could be computed as localCount * 2 (+ 1 if appLocalPath != null).
        //   However, this would suppose that each package Before and After are both local or not (that is theoretically 
        //   true for regular packages).
        //   We don't assume this: the localResources array is built as a selection of the allPackageResources (that
        //   also consider the topological order that must be handled).
        var resourceIndexSize = 1 + descriptorPackageCount * 2;
        // If the AppResourcesLocalPath is specified, then the tailPackage is a local package
        // and the resource index has one more entry for the app local FileSystemResourceContainer.
        if( appLocalPath != null )
        {
            ++resourceIndexSize;
            ++localCount;
        }
        var bLocal = ImmutableArray.CreateBuilder<ResPackage>( localCount );
        // We can initialize now the packageIndex and the resourceIndex.
        var packageIndex = new Dictionary<object, ResPackage>( packageIndexSize );
        var resourceIndex = new Dictionary<IResourceContainer, IResPackageResources>( resourceIndexSize );

        // Initialize the ResourceSpaceData instance on our mutable packageIndex.
        var excludedOptionalResourcePaths = finalOptionalPackages.SelectMany( p => p.ResourcesInnerContainer
                                                                                    .AllResources
                                                                                    .Select( r => r.GetNormalizedResourceName() ) )
                                                                 .ToImmutableArray();
        var space = new ResCoreData( _generatedCodeContainer,
                                      _collector.LiveStatePath,
                                      _collector.TypeCache,
                                      packageIndex,
                                      resourceIndex,
                                      codeHandledResources,
                                      excludedOptionalResourcePaths );

        // Initialize the SpaceDataCacheBuilder. It carries the space data to the ResPackage constructors.
        var dataCacheBuilder = new ResCoreDataCacheBuilder( space,
                                                          descriptorPackageCount,
                                                          _collector.LocalPackageCount,
                                                          appLocalPath != null );

       // Create the code package and adds it where it must be.
        ResPackage codePackage = CreateCodePackage( dataCacheBuilder, _generatedCodeContainer );
        bAll.Add( codePackage );
        packageIndex.Add( codePackage.FullName, codePackage );
        Throw.DebugAssert( codePackage.Resources.Index == 0 );
        allPackageResources[0] = codePackage.Resources;
        Throw.DebugAssert( codePackage.AfterResources.Index == 1 );
        allPackageResources[1] = codePackage.AfterResources;
        Throw.DebugAssert( codePackage.AfterResources.Resources == _generatedCodeContainer
                            || codePackage.AfterResources.Resources is ResourceContainerWrapper );
        resourceIndex.Add( codePackage.AfterResources.Resources, codePackage.AfterResources );
        // We track the number of local IResPackageResources.
        Throw.DebugAssert( codePackage.Resources.LocalPath == null && codePackage.AfterResources.LocalPath == null );
        int localPackageResourceCount = 0;

        // This is the common requirements of all ResPackage that have no requirement.
        ImmutableArray<ResPackage> requiresCode = [codePackage];
        // The Watch root is the longest common parent of all the ResPackage.LocalPath.
        // We compute it if and only if the LiveStatePath is not ResourceSpaceCollector.NoLiveState.
        string? watchRoot = null;

        var bAppRequirements = ImmutableArray.CreateBuilder<ResPackage>();
        foreach( var d in _collector.Packages )
        {
            Throw.DebugAssert( d.Resources is StoreContainer && d.AfterResources is StoreContainer );
            // Close the CodeGen resources (if they are code generated).
            if( d.ResourcesInnerContainer is CodeGenResourceContainer c1 ) c1.Close();
            if( d.AfterResourcesInnerContainer is CodeGenResourceContainer c2 ) c2.Close();

            // Requirements and children have already been indexed because the collector packages are sorted.
            ImmutableArray<ResPackage> requires;
            ImmutableArray<ResPackage> children;
            if( d._requires == null )
            {
                requires = requiresCode;
            }
            else
            {
                Throw.DebugAssert( d._requires.All( r => r.IsValid && !r.IsOptional ) );
                requires = d._requires.Select( r => packageIndex[r.FullName!] ).ToImmutableArray();
            }
            if( d._children == null )
            {
                children = ImmutableArray<ResPackage>.Empty;
            }
            else
            {
                Throw.DebugAssert( d._children.All( r => r.IsValid && !r.IsOptional ) );
                children = d._children.Select( r => packageIndex[r.FullName!] ).ToImmutableArray();
            }
            Throw.DebugAssert( "A child cannot be required and a requirement cannot be a child.",
                               !requires.Intersect( children ).Any() );

            var p = new ResPackage( dataCacheBuilder,
                                    d.FullName,
                                    d.DefaultTargetPath,
                                    d._idxHeader + 2,
                                    d.Resources,
                                    d._idxFooter + 2,
                                    d.AfterResources,
                                    d.IsGroup,
                                    d.Type,
                                    requires,
                                    children,
                                    bAll.Count );
            bAll.Add( p );
            if( p.IsLocalPackage )
            {
                if( p.Resources.LocalPath != null ) ++localPackageResourceCount;
                if( p.AfterResources.LocalPath != null ) ++localPackageResourceCount;
                bLocal.Add( p );
            }
            // Index it.
            packageIndex.Add( p.FullName, p );
            if( p.Type != null )
            {
                packageIndex.Add( p.Type, p );
            }
            var mappings = d.SingleMappings;
            if( mappings != null )
            {
                foreach( var m in mappings )
                {
                    packageIndex.Add( m, p );
                }
            }
            // Index the actual container, not the StoreContainer that is "transparent".
            // This is why on the ResSpaceData, there's no GetPackageResources( IResourceContainer )
            // but only GetPackageResources( ResourceLocator ) and GetPackageResources( ResourceFolder ):
            // the IResPackageResources.Resources cannot be found, only their inner containers can and these
            // are the resource's locator and folder containers.
            Throw.DebugAssert( p.Resources.Resources == d.Resources && p.AfterResources.Resources == d.AfterResources);
            resourceIndex.Add( d.ResourcesInnerContainer, p.Resources );
            resourceIndex.Add( d.AfterResourcesInnerContainer, p.AfterResources );

            // Enlist the package resources.
            Throw.DebugAssert( allPackageResources[p.Resources.Index] == null );
            allPackageResources[p.Resources.Index] = p.Resources;
            Throw.DebugAssert( allPackageResources[p.AfterResources.Index] == null );
            allPackageResources[p.AfterResources.Index] = p.AfterResources;
            // Track the watch root.
            var local = p.Resources.LocalPath ?? p.AfterResources.LocalPath;
            if( local != null && _collector.LiveStatePath != ResSpaceCollector.NoLiveState )
            {
                Throw.DebugAssert( local.EndsWith( Path.DirectorySeparatorChar ) );
                if( watchRoot == null )
                {
                    watchRoot = local.Substring( 0, local.LastIndexOf( Path.DirectorySeparatorChar, local.Length - 2) + 1 );
                }
                else
                {
                    watchRoot = CommonParentPath( watchRoot, local );
                }
                Throw.DebugAssert( watchRoot.EndsWith( Path.DirectorySeparatorChar ) );
            }
            if( !d._hasIncomingDeps )
            {
                bAppRequirements.Add( p );
            }
        }
        // We now can initialize the "<App>" package.
        // The FileSystemResourceContainer normalizes the path (ends with Path.DirectorySeparator)
        // and this must be normalized!
        var appPackage = CreateAppPackage( ref appLocalPath,
                                           dataCacheBuilder,
                                           bAppRequirements.Count > 0 ? bAppRequirements.DrainToImmutable() : requiresCode,
                                           bAll.Count );
        bAll.Add( appPackage );
        packageIndex.Add( appPackage.FullName, appPackage );
        Throw.DebugAssert( appPackage.Resources.Index == allPackageResources.Length - 2 );
        allPackageResources[^2] = appPackage.Resources;
        Throw.DebugAssert( appPackage.AfterResources.Index == allPackageResources.Length - 1 );
        allPackageResources[^1] = appPackage.AfterResources;
        if( appLocalPath != null )
        {
            // If we have no local packages, watchRoot is null: we'll only
            // watch the "<App> folder.
            watchRoot ??= appLocalPath;
            resourceIndex.Add( appPackage.Resources.Resources, appPackage.Resources );
            bLocal.Add( appPackage );
            ++localPackageResourceCount;
        }
        if( monitor.ShouldLogLine( LogLevel.Debug, null, out _ ) )
        {
            using( monitor.OpenDebug( "TypeScript packages structure:" ) )
            {
                monitor.Debug(
                    bAll.Skip( 1 )
                        .Select( x => $"""
                           {(x.IsLocalPackage ? "(local) " : "        ")}{x} => {x.Requires.Select( r => r.ToString() ).Concatenate()}{string.Concat( x.Children.Select( c => $"{Environment.NewLine}{new string( ' ', 16 )} |{c}" ) )}
                           """ )
                                   .Concatenate( Environment.NewLine ) );
            }
        }

        Throw.DebugAssert( "Expected size reached.", packageIndex.Count == packageIndexSize );
        Throw.DebugAssert( "Expected size reached.", resourceIndex.Count == resourceIndexSize );
        var packages = bAll.MoveToImmutable();
        space._packages = packages;
        space._localPackages = bLocal.MoveToImmutable();
        Throw.DebugAssert( allPackageResources.All( r => r != null ) );
        space._allPackageResources = ImmutableCollectionsMarshal.AsImmutableArray( allPackageResources );

        Throw.DebugAssert( allPackageResources.Count( r => r.LocalPath != null ) == localPackageResourceCount );
        var bLocalPackageResources = ImmutableArray.CreateBuilder<IResPackageResources>( localPackageResourceCount );
        foreach( var r in allPackageResources )
        {
            if( r.LocalPath != null )
            {
                ((ResPackage.IResPackageData)r).SetLocalIndex( bLocalPackageResources.Count );
                bLocalPackageResources.Add( r );
            }
        }
        Throw.DebugAssert( bLocalPackageResources.Count == localPackageResourceCount );
        space._localPackageResources = bLocalPackageResources.MoveToImmutable();

        space._codePackage = codePackage;
        space._appPackage = appPackage;
        Throw.DebugAssert( _collector.LiveStatePath == ResSpaceCollector.NoLiveState
                           || (space._localPackages.Length != 0) == (watchRoot != null) );
        space._watchRoot = watchRoot;
        // The space is initialized with all its packages.
        // The ReachablePackageCacheBuilder has collected all the possible Reachable packages, we can now
        // compute the aggregation sets.
        space._resPackageDataCache = dataCacheBuilder.Build( monitor, packages, watchRoot != null );
        // Post conditions:
        Throw.DebugAssert( "<Code> can reach nothing.", codePackage.AfterReachables.Count == 0 );
        Throw.DebugAssert( "<Code> can be reached from any packages.",
                           packages.Where( p => p != codePackage ).All( p => p.AfterReachables.Contains( codePackage ) ) );
        Throw.DebugAssert( "All packages can be reached from <App>.",
                           appPackage.AfterReachables.SetEquals( packages.Where( p => p != appPackage ) ) );
        Throw.DebugAssert( "Packages have both resources either stable or local (except the AppPackage.AfterResources that is empty by design).",
                           packages.All( p => p.IsLocalPackage == (p.Resources.LocalPath != null)
                                              && (p == space.AppPackage || p.IsLocalPackage == (p.AfterResources.LocalPath != null) ) ) );
        return new ResSpaceData( space, codeHandledResources, finalOptionalPackages );
    }

    static string CommonParentPath( string path1, string path2 )
    {
        string[] p1 = path1.Split( Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries );
        string[] p2 = path2.Split( Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries );
        int len = p1.Length;
        if( len > p2.Length )
        {
            (p1, p2) = (p2, p1);
            len = p1.Length;
        }
        int iCommon = 0;
        while( iCommon < len && p1[iCommon] == p2[iCommon] ) ++iCommon;
        return iCommon == 0
                 ? string.Empty
                 : iCommon == len
                    ? path1
                    : string.Join( Path.DirectorySeparatorChar, p1.Take( iCommon ) ) + Path.DirectorySeparatorChar;
    }
}

