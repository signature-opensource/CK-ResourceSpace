using CK.EmbeddedResources;
using CK.Engine.TypeCollector;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace CK.Core;

/// <summary>
/// Builder for <see cref="ResSpaceCollector"/> that is the first step to produce a
/// <see cref="ResSpace"/>. 
/// <para>
/// This initial builder collects all the fundamental information required to fully update a target
/// code generated folder including the Live state.
/// </para>
/// </summary>
public sealed class ResSpaceConfiguration : IResPackageDescriptorRegistrar
{
    readonly CoreCollector _coreCollector;
    IResourceContainer? _generatedCodeContainer;
    string? _appResourcesLocalPath;
    string? _liveStatePath;

    /// <summary>
    /// Initializes a new configuration. A new empty <see cref="TypeCache"/> is created and
    /// no restriction exist on types.
    /// </summary>
    public ResSpaceConfiguration()
        : this( new GlobalTypeCache(), null )
    {
    }

    /// <summary>
    /// Initializes a new configuration bound to a type cache and a filtered type set.
    /// </summary>
    /// <param name="typeCache">The type cache that will be used.</param>
    /// <param name="allowedTypes">
    /// Optional type set restriction.
    /// When false is returned by this predicate, <see cref="RegisterPackage(IActivityMonitor, ICachedType, NormalizedPath, bool?, bool)"/> fails.
    /// </param>
    public ResSpaceConfiguration( GlobalTypeCache typeCache, Func<ICachedType, bool>? allowedTypes = null )
    {
        _coreCollector = new CoreCollector( typeCache, allowedTypes );
    }

    /// <summary>
    /// Gets or sets the "&lt;Code&gt;" head package generated resource container.
    /// When let to null, an empty container is considered until a non null container is configured
    /// with <see cref="ResSpaceCollector.GeneratedCodeContainer"/>, <see cref="ResSpaceDataBuilder.GeneratedCodeContainer"/>,
    /// <see cref="ResSpaceData.GeneratedCodeContainer"/> or (last chance) with <see cref="ResSpaceBuilder.GeneratedCodeContainer"/>.
    /// <para>
    /// This possibly late assignation of the code generated container enables code generator to be able to generate code
    /// based on the topologically ordered <see cref="ResCoreData.Packages"/>. Not all code generators require this:
    /// some can assign the code from this initial configuration. 
    /// </para>
    /// </summary>
    [DisallowNull]
    public IResourceContainer? GeneratedCodeContainer
    {
        get => _generatedCodeContainer;
        set
        {
            Throw.CheckNotNullArgument( value );
            _generatedCodeContainer = value;
        }
    }

    /// <summary>
    /// Gets or sets the path of the "&lt;App&gt;" tail package application local resources 
    /// (the <see cref="ResCoreData.AppPackage"/>'s <see cref="ResPackage.Resources"/>' <see cref="IResPackageResources.LocalPath"/>).
    /// When let to null, an empty container is used.
    /// <para>
    /// When not null, this path is fully qualified and ends with <see cref="Path.DirectorySeparatorChar"/>
    /// and will be created if it doesn't exist.
    /// </para>
    /// </summary>
    public string? AppResourcesLocalPath
    {
        get => _appResourcesLocalPath;
        set
        {
            if( value != null )
            {
                Throw.CheckArgument( Path.IsPathFullyQualified( value ) );
                value = Path.GetFullPath( value );
                if( value[^1] != Path.DirectorySeparatorChar )
                {
                    value += Path.DirectorySeparatorChar;
                }
            }
            _appResourcesLocalPath = value;
        }
    }

    /// <summary>
    /// Gets or sets the folder that contains the Live state.
    /// This is optional as this defaults to "<see cref="AppResourcesLocalPath"/>/.ck-watch/".
    /// It will be created if it doesn't exist.
    /// <para>
    /// By setting it to <see cref="ResSpaceCollector.NoLiveState"/>, no Live state is created
    /// even if <see cref="AppResourcesLocalPath"/> is specified.
    /// </para>
    /// </summary>
    public string? LiveStatePath
    {
        get => _liveStatePath;
        set
        {
            if( value != null )
            {
                if( value != ResSpaceCollector.NoLiveState )
                {
                    Throw.DebugAssert( ResSpaceCollector.NoLiveState == "none" );
                    Throw.CheckArgument( """LiveStatePath must be "none" or a fully qualified path.""", Path.IsPathFullyQualified( value ) );
                    value = Path.GetFullPath( value );
                    if( value[^1] != Path.DirectorySeparatorChar )
                    {
                        value += Path.DirectorySeparatorChar;
                    }
                }
            }
            _liveStatePath = value;
        }
    }

    /// <summary>
    /// Gets or sets the optional <see cref="IResPackageDescriptorResolver"/>.
    /// When let to null, the initial set of packages must contain all the non-optional packages.
    /// </summary>
    public IResPackageDescriptorResolver? PackageResolver
    {
        get => _coreCollector._packageResolver;
        set => _coreCollector._packageResolver = value;
    }

    /// <summary>
    /// Gets or sets whether the topological sort uses ascending or descending order
    /// of the <see cref="ResPackageDescriptor.FullName"/> to guaranty determinism of
    /// the final ordering.
    /// <para>
    /// If, when reverting the order, <see cref="ResSpaceDataBuilder.Build(IActivityMonitor)"/> fails,
    /// this means that a there is a missing topological constraint in the graph.
    /// </para>
    /// </summary>
    public bool RevertOrderingNames
    {
        get => _coreCollector._revertOrderingNames;
        set => _coreCollector._revertOrderingNames = value;
    }

    /// <inheritdoc />
    public GlobalTypeCache TypeCache => _coreCollector.TypeCache;

    /// <inheritdoc />
    public ResPackageDescriptor? FindByFullName( string fullName ) => _coreCollector.FindByFullName( fullName );

    /// <inheritdoc />
    public ResPackageDescriptor? FindByType( ICachedType type ) => _coreCollector.FindByType( type );

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
    /// <summary>
    /// Produces the <see cref="ResSpaceCollector"/> that can be used to continue to register packages and
    /// eventually be handles by a <see cref="ResSpaceDataBuilder"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The collector with initialized packages or null on error.</returns>
    public ResSpaceCollector? Build( IActivityMonitor monitor )
    {
        var liveStatePath = _liveStatePath
                                        ?? (_appResourcesLocalPath == null
                                                ? ResSpaceCollector.NoLiveState
                                                : _appResourcesLocalPath + ".ck-watch" + Path.DirectorySeparatorChar);
        return new ResSpaceCollector( _coreCollector,
                                      _generatedCodeContainer,
                                      _appResourcesLocalPath,
                                      liveStatePath );
    }

}
