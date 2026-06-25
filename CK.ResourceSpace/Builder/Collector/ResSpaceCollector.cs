using CK.EmbeddedResources;
using CK.Engine.TypeCollector;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CK.Core;

/// <summary>
/// Exposes mutable <see cref="ResPackageDescriptor"/> for a resource space.
/// This enable resource packages to be registered from a type (that must be decorated
/// with at least one <see cref="IEmbeddedResourceTypeAttribute"/> attribute) or from any <see cref="IResourceContainer"/>
/// in any order.
/// <para>
/// This is the input of the <see cref="ResSpaceDataBuilder"/> that topologically sorts
/// the descriptors to produce the immutable <see cref="ResCoreData.Packages"/>.
/// </para>
/// </summary>
public sealed class ResSpaceCollector : IResPackageDescriptorRegistrar
{
    /// <summary>
    /// Marker for <see cref="LiveStatePath"/> when no Live state must be created.
    /// </summary>
    public const string NoLiveState = "none";

    readonly CoreCollector _coreCollector;
    readonly string? _appResourcesLocalPath;
    readonly string _liveStatePath;
    IResourceContainer? _generatedCodeContainer;

    internal ResSpaceCollector( CoreCollector coreCollector,
                                IResourceContainer? generatedCodeContainer,
                                string? appResourcesLocalPath,
                                string liveStatePath )
    {
        _coreCollector = coreCollector;
        _generatedCodeContainer = generatedCodeContainer;
        _appResourcesLocalPath = appResourcesLocalPath;
        _liveStatePath = liveStatePath;
    }

    internal bool CloseRegistrations( IActivityMonitor monitor,
                                      out HashSet<ResourceLocator> codeHandledResources,
                                      out IReadOnlyList<ResPackageDescriptor> finalOptionalPackages )
    {
        return _coreCollector.Close( monitor, out codeHandledResources, out finalOptionalPackages );
    }

    /// <summary>
    /// Gets the package index. <see cref="FindByFullName(string)"/> or <see cref="FindByType(ICachedType)"/>
    /// should be used to find a package descriptor.
    /// </summary>
    public IReadOnlyDictionary<object, ResPackageDescriptor> PackageIndex => _coreCollector.PackageIndex;

    /// <summary>
    /// Gets the package descriptors.
    /// <para>
    /// This doesn't contain the "&lt;Code&gt;" and "&lt;App&gt;" packages: the <see cref="ResSpaceDataBuilder"/>
    /// generates them in the <see cref="ResCoreData"/>.
    /// </para>
    /// </summary>
    public IReadOnlyCollection<ResPackageDescriptor> Packages => _coreCollector.Packages;

    /// <summary>
    /// Gets the number of local packages in <see cref="Packages"/> (excluding <see cref="AppResourcesLocalPath"/>).
    /// </summary>
    public int LocalPackageCount => _coreCollector.LocalPackageCount;

    /// <summary>
    /// Gets the number of packages that are defined by a <see cref="ResPackageDescriptor.Type"/>.
    /// </summary>
    public int TypedPackageCount => _coreCollector.TypedPackageCount;

    internal int SingleMappingCount => _coreCollector.SingleMappingCount;

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
    /// Gets the path of the application local resources. See <see cref="ResSpaceConfiguration.AppResourcesLocalPath"/>.
    /// </summary>
    public string? AppResourcesLocalPath => _appResourcesLocalPath;

    /// <summary>
    /// Gets the folder that contains the Live state.
    /// <see cref="NoLiveState"/> if no Live state must be created.
    /// See <see cref="ResSpaceConfiguration.LiveStatePath"/>.
    /// </summary>
    public string LiveStatePath => _liveStatePath;

    /// <inheritdoc />
    public GlobalTypeCache TypeCache => _coreCollector.TypeCache;

    /// <inheritdoc />
    public ResPackageDescriptor? FindByFullName( string fullName ) => PackageIndex.GetValueOrDefault( fullName );

    /// <inheritdoc />
    public ResPackageDescriptor? FindByType( ICachedType type ) => PackageIndex.GetValueOrDefault( type );

    /// <inheritdoc />
    public ResPackageDescriptor? RegisterPackage( IActivityMonitor monitor,
                                                  string fullName,
                                                  NormalizedPath defaultTargetPath,
                                                  IResourceContainer resourceStore,
                                                  IResourceContainer resourceAfterStore,
                                                  bool? isOptional )
    {
        return _coreCollector.RegisterPackage( monitor, fullName, defaultTargetPath, resourceStore, resourceAfterStore, isOptional );
    }

    /// <inheritdoc />
    public ResPackageDescriptor? RegisterPackage( IActivityMonitor monitor,
                                                  ICachedType type,
                                                  NormalizedPath defaultTargetPath,
                                                  bool? isOptional = null,
                                                  bool ignoreLocal = false )
    {
        return _coreCollector.RegisterPackage( monitor, type, defaultTargetPath, isOptional, ignoreLocal );
    }

    /// <inheritdoc />
    public void RemoveCodeHandledResource( ResPackageDescriptor package, ResourceLocator resource )
    {
        _coreCollector.RemoveCodeHandledResource( package, resource );
    }

    /// <inheritdoc />
    public bool RemoveExpectedCodeHandledResource( IActivityMonitor monitor, ResPackageDescriptor package, string resourceName, out ResourceLocator resource )
    {
        return _coreCollector.RemoveExpectedCodeHandledResource( monitor, package, resourceName, out resource );
    }

    /// <inheritdoc />
    public bool RemoveCodeHandledResource( ResPackageDescriptor package, string resourceName, out ResourceLocator resource )
    {
        return _coreCollector.RemoveCodeHandledResource( package, resourceName, out resource );
    }
}
