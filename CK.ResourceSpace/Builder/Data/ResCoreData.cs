using CK.EmbeddedResources;
using CK.Engine.TypeCollector;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace CK.Core;

/// <summary>
/// <see cref="ResSpace"/> core data is produced by the <see cref="ResSpaceDataBuilder"/> and 
/// contains the final <see cref="ResPackage"/> topologically ordered.
/// <para>
/// This can now be consumed by a <see cref="ResSpaceBuilder"/> that can be configured with resource handlers
/// to eventually produce a <see cref="ResSpace"/>.
/// </para>
/// </summary>
public sealed partial class ResCoreData
{
    readonly IReadOnlyDictionary<object, ResPackage> _packageIndex;
    readonly IReadOnlyDictionary<IResourceContainer, IResPackageResources> _resourceIndex;
    // Readonly "hidden" resources here: ResSpaceData has a HashSet refererence on it.
    readonly IReadOnlySet<ResourceLocator> _codeHandledResources;
    readonly GlobalTypeCache _typeCache;
    readonly string _liveStatePath;
    readonly ImmutableArray<string> _excludedOptionalResourcePaths;

    // Last mutable code container (settable through ResSpaceData).
    // Duplicating it is required, because even if we inspect the ResourceContainerWrapper.InnerContainer,
    // an empty container may be a non yet assigned container or a definitly assigned empty one.
    internal IResourceContainer? _generatedCodeContainer;

    // _packages, _localPackages, _allPackageResources, _localPackageResources, _exposedPackages, _reachablePackageSetCache,
    // _codePackage, _appPackage and _watchRoot are set by the ResourceSpaceDataBuilder.Build method.
    internal ImmutableArray<ResPackage> _packages;
    internal ImmutableArray<ResPackage> _localPackages;
    internal ImmutableArray<IResPackageResources> _allPackageResources;
    internal ImmutableArray<IResPackageResources> _localPackageResources;
    [AllowNull]internal ICoreDataCache _resPackageDataCache;
    [AllowNull]internal ResPackage _codePackage;
    [AllowNull]internal ResPackage _appPackage;
    internal string? _watchRoot;

    internal ResCoreData( IResourceContainer? generatedCodeContainer,
                          string cKWatchFolderPath,
                          GlobalTypeCache typeCache,
                          IReadOnlyDictionary<object, ResPackage> packageIndex,
                          IReadOnlyDictionary<IResourceContainer, IResPackageResources> resourceIndex,
                          IReadOnlySet<ResourceLocator> codeHandledResources,
                          ImmutableArray<string> excludedOptionalResourcePaths )
    {
        _generatedCodeContainer = generatedCodeContainer;
        _liveStatePath = cKWatchFolderPath;
        _typeCache = typeCache;
        _packageIndex = packageIndex;
        _resourceIndex = resourceIndex;
        _codeHandledResources = codeHandledResources;
        _excludedOptionalResourcePaths = excludedOptionalResourcePaths;
    }

    /// <summary>
    /// Gets the packages indexed by their <see cref="ResPackage.FullName"/> and <see cref="ResPackage.Type"/> (if
    /// the package is defined by a type).
    /// </summary>
    public IReadOnlyDictionary<object, ResPackage> PackageIndex => _packageIndex;

    /// <summary>
    /// Gets the "&lt;Code&gt;" head package.
    /// </summary>
    public ResPackage CodePackage => _codePackage;

    /// <summary>
    /// Gets the packages topologically ordered. <see cref="ResPackage.Index"/> is the index in this array.
    /// <para>
    /// This first package is <see cref="CodePackage"/> and the last one is <see cref="AppPackage"/>.
    /// </para>
    /// </summary>
    public ImmutableArray<ResPackage> Packages => _packages;

    /// <summary>
    /// Gets the local packages.
    /// </summary>
    public ImmutableArray<ResPackage> LocalPackages => _localPackages;

    /// <summary>
    /// Gets the "&lt;App&gt;" tail package.
    /// <para>
    /// When <see cref="ResSpaceCollector.AppResourcesLocalPath"/> is defined, its
    /// <see cref="ResPackage.Resources"/>.<see cref="IResPackageResources.LocalPath">LocalPath</see>
    /// is the AppResourcesLocalPath.
    /// </para>
    /// </summary>
    public ResPackage AppPackage => _appPackage;

    /// <summary>
    /// Gets all the topologically ordered <see cref="IResPackageResources"/> indexed
    /// by their <see cref="IResPackageResources.Index"/>.
    /// </summary>
    public ImmutableArray<IResPackageResources> AllPackageResources => _allPackageResources;

    /// <summary>
    /// Gets the topologically ordered local <see cref="IResPackageResources"/> indexed
    /// by their <see cref="IResPackageResources.LocalIndex"/>.
    /// </summary>
    public ImmutableArray<IResPackageResources> LocalPackageResources => _localPackageResources;

    /// <summary>
    /// Tries to get the <see cref="IResPackageResources"/> from a <see cref="ResourceLocator"/>.
    /// </summary>
    /// <param name="locator">The resource.</param>
    /// <param name="packageResources">The corresponding package resources.</param>
    /// <returns>True when found, false otherwise.</returns>
    public bool TryGetPackageResources( ResourceLocator locator, [NotNullWhen( true )] out IResPackageResources? packageResources )
    {
        return _resourceIndex.TryGetValue( locator.Container, out packageResources );
    }

    /// <summary>
    /// Tries to get the <see cref="IResPackageResources"/> from a <see cref="ResourceFolder"/>.
    /// </summary>
    /// <param name="folder">The resource.</param>
    /// <param name="packageResources">The corresponding package resources.</param>
    /// <returns>True when found, false otherwise.</returns>
    public bool TryGetPackageResources( ResourceFolder folder, [NotNullWhen( true )] out IResPackageResources? packageResources )
    {
        return _resourceIndex.TryGetValue( folder.Container, out packageResources );
    }

    /// <summary>
    /// Gets the <see cref="IResPackageResources"/> from a <see cref="ResourceLocator"/> or
    /// throws an <see cref="System.ArgumentException"/>.
    /// </summary>
    /// <param name="locator">The resource.</param>
    /// <returns>The corresponding package resources.</returns>
    public IResPackageResources GetPackageResources( ResourceLocator locator )
    {
        if( !_resourceIndex.TryGetValue( locator.Container, out var packageResources ) )
        {
            Throw.ArgumentException( $"Container for {locator} not found among {_packages.Length} packages." );
        }
        return packageResources;
    }

    /// <summary>
    /// Gets the <see cref="IResPackageResources"/> from a <see cref="ResourceFolder"/> or throws an <see cref="System.ArgumentException"/>.
    /// </summary>
    /// <param name="folder">The resource folder.</param>
    /// <returns>The corresponding package resources.</returns>
    public IResPackageResources GetPackageResources( ResourceFolder folder )
    {
        if( !_resourceIndex.TryGetValue( folder.Container, out var packageResources ) )
        {
            Throw.ArgumentException( $"Container for {folder} not found among {_packages.Length} packages." );
        }
        return packageResources;
    }

    /// <summary>
    /// Gets the set of resources that are handled by codes: these resources don't appear in the
    /// <see cref="IResPackageResources.Resources"/> <see cref="ResPackage.Resources"/> and
    /// <see cref="ResPackage.AfterResources"/>.
    /// </summary>
    public IReadOnlySet<ResourceLocator> CodeHandledResources => _codeHandledResources;

    /// <summary>
    /// Gets the <see cref="ResourceLocator.ResourceName"/> where path separators are normalized to '/'
    /// of all the <see cref="ResSpaceData.FinalOptionalPackages"/>.
    /// </summary>
    public ImmutableArray<string> ExcludedOptionalResourcePaths => _excludedOptionalResourcePaths;

    /// <summary>
    /// Gets the type cache.
    /// </summary>
    public GlobalTypeCache TypeCache => _typeCache;

    /// <summary>
    /// Gets the cache from wich <see cref="ResPackageDataCache{T}"/> can be built.
    /// </summary>
    public ICoreDataCache SpaceDataCache => _resPackageDataCache;

    /// <summary>
    /// Gets the folder that contains the Live state.
    /// Can be <see cref="ResSpaceCollector.NoLiveState"/>.
    /// </summary>
    public string LiveStatePath => _liveStatePath;

    /// <summary>
    /// Gets whether this space has a Live state. This is true and <see cref="WatchRoot"/> is not null if:
    /// <list type="bullet">
    ///     <item>
    ///     At least one local packages exists,
    ///     <para>
    ///     or the "&lt;App&gt;" package has a defined folder (<see cref="ResSpaceCollector.AppResourcesLocalPath"/> was set).
    ///     </para>
    ///     </item>
    ///     <item>
    ///     And <see cref="LiveStatePath"/> is not the <see cref="ResSpaceCollector.NoLiveState"/> string.
    ///     </item>
    /// </list>
    /// </summary>
    [MemberNotNullWhen( true, nameof( WatchRoot ) )]
    public bool HasLiveState => _watchRoot != null;

    /// <summary>
    /// Gets the watch root when <see cref="HasLiveState"/> is true. Null otherwise.
    /// </summary>
    public string? WatchRoot => _watchRoot;
}
