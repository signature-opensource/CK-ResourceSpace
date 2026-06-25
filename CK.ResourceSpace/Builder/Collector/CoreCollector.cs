using CK.EmbeddedResources;
using CK.Engine.TypeCollector;
using System;
using System.Collections.Generic;

namespace CK.Core;

sealed partial class CoreCollector : IResPackageDescriptorRegistrar
{
    readonly GlobalTypeCache _typeCache;
    readonly Func<ICachedType, bool>? _allowedTypes;

    // Packages are indexed by their FullName, their ICachedType if package is defined
    // by type and by their Resources Store container.
    // The IResourceContainer key is used by this builder only to check that no resource
    // containers are shared by 2 packages.
    readonly Dictionary<object, ResPackageDescriptor> _packageIndex;
    readonly List<ResPackageDescriptor> _packages;
    // Contains the package initially optional. After the topological
    // sort, they may be no more optional: the TopologicalSorter.Run() 
    // removes the eventually non optional packages from this list.
    readonly List<ResPackageDescriptor> _optionalPackages;
    readonly ResPackageDescriptorContext _packageDescriptorContext;

    // Set by ResSpaceConfiguration.
    internal IResPackageDescriptorResolver? _packageResolver;
    internal bool _revertOrderingNames;

    int _localPackageCount;
    int _typedPackageCount;

    public CoreCollector( GlobalTypeCache typeCache, Func<ICachedType, bool>? allowedTypes )
    {
        _packageIndex = new Dictionary<object, ResPackageDescriptor>();
        _packages = new List<ResPackageDescriptor>();
        _optionalPackages = new List<ResPackageDescriptor>();
        _packageDescriptorContext = new ResPackageDescriptorContext( _packageIndex, typeCache );
        _typeCache = typeCache;
        _allowedTypes = allowedTypes;
    }

    public ResPackageDescriptor? FindByFullName( string fullName ) => _packageIndex.GetValueOrDefault( fullName );

    public ResPackageDescriptor? FindByType( ICachedType type ) => _packageIndex.GetValueOrDefault( type );

    public ResPackageDescriptor? RegisterPackage( IActivityMonitor monitor,
                                                  string fullName,
                                                  NormalizedPath defaultTargetPath,
                                                  IResourceContainer resourceStore,
                                                  IResourceContainer resourceAfterStore,
                                                  bool? isOptional )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( fullName );
        Throw.CheckArgument( resourceStore is not null && resourceStore.IsValid );
        Throw.CheckArgument( resourceAfterStore is not null && resourceStore.IsValid );
        Throw.CheckArgument( resourceStore != resourceAfterStore );
        return DoRegister( monitor, fullName, null, defaultTargetPath, resourceStore, resourceAfterStore, isOptional );
    }

    public ResPackageDescriptor? RegisterPackage( IActivityMonitor monitor,
                                                  ICachedType type,
                                                  NormalizedPath defaultTargetPath,
                                                  bool? isOptional,
                                                  bool ignoreLocal )
    {
        Throw.CheckNotNullArgument( type );
        Throw.CheckArgument( type.EngineUnhandledType == EngineUnhandledType.None );
        Throw.CheckArgument( "Type cache mismatch.", type.TypeCache == TypeCache );
        if( _packageIndex.TryGetValue( type, out var already ) )
        {
            monitor.Error( $"Duplicate package registration: Type '{type.CSharpName}' is already registered as '{already}'." );
            return null;
        }
        if( _allowedTypes != null && !_allowedTypes.Invoke( type ) )
        {
            monitor.Error( $"Type '{type.CSharpName}' cannot be registered, it doesn't belong to the allowed type set." );
            return null;
        }
        IResourceContainer resourceStore = type.Type.CreateResourcesContainer( monitor, ignoreLocal: ignoreLocal );
        IResourceContainer resourceAfterStore = type.Type.CreateResourcesContainer( monitor, resAfter: true, ignoreLocal: ignoreLocal );
        return resourceStore.IsValid && resourceAfterStore.IsValid
            ? DoRegister( monitor, type.Type.FullName!, type, defaultTargetPath, resourceStore, resourceAfterStore, isOptional )
            : null;
    }

    ResPackageDescriptor? DoRegister( IActivityMonitor monitor,
                                      string fullName,
                                      ICachedType? type,
                                      NormalizedPath defaultTargetPath,
                                      IResourceContainer resourceStore,
                                      IResourceContainer resourceAfterStore,
                                      bool? isOptional )
    {
        if( _packageIndex.TryGetValue( fullName, out var already ) )
        {
            monitor.Error( $"Duplicate package registration: FullName is already registered as '{already}'." );
            return null;
        }
        if( _packageIndex.TryGetValue( resourceStore, out already ) )
        {
            monitor.Error( $"Package resources mismatch: {resourceStore} cannot be associated to '{fullName}' as it is already associated to '{already}'." );
            return null;
        }
        if( _packageIndex.TryGetValue( resourceAfterStore, out already ) )
        {
            monitor.Error( $"Package resources mismatch: {resourceAfterStore} cannot be associated to '{fullName}' as it is already associated to '{already}'." );
            return null;
        }

        var p = new ResPackageDescriptor( _packageDescriptorContext,
                                          fullName,
                                          type,
                                          defaultTargetPath,
                                          resources: new StoreContainer( _packageDescriptorContext, resourceStore ),
                                          afterResources: new StoreContainer( _packageDescriptorContext, resourceAfterStore ) );
        bool initialized = type != null
                            ? p.InitializeFromType( monitor, isOptional )
                            : p.InitializeFromManifest( monitor, isOptional );
        if( !initialized )
        {
            return null;
        }
        if( p.IsOptional )
        {
            _optionalPackages.Add( p );
        }
        else
        {
            _packages.Add( p );
        }
        // Whether the package is optional or not, we index it.
        // An optional package will be removed if it is still optional after the
        // topological sort (RemoveDefinitelyOptional below).
        if( p.IsLocalPackage )
        {
            ++_localPackageCount;
        }
        _packageIndex.Add( fullName, p );
        _packageIndex.Add( resourceStore, p );
        _packageIndex.Add( resourceAfterStore, p );
        if( type != null )
        {
            ++_typedPackageCount;
            _packageIndex.Add( type, p );
        }
        return p;
    }

    public void RemoveCodeHandledResource( ResPackageDescriptor package, ResourceLocator resource )
    {
        Throw.CheckArgument( "Package/Context mismatch.", package.Context == _packageDescriptorContext );
        Throw.CheckArgument( "Resource/Package mismatch.", resource.Container == package.Resources || resource.Container == package.AfterResources );
        _packageDescriptorContext.RegisterCodeHandledResources( resource );
    }

    public bool RemoveExpectedCodeHandledResource( IActivityMonitor monitor, ResPackageDescriptor package, string resourceName, out ResourceLocator resource )
    {
        Throw.CheckArgument( "Package/Context mismatch.", package.Context == _packageDescriptorContext );
        if( !package.ResourcesInnerContainer.TryGetExpectedResource( monitor,
                                                                       resourceName,
                                                                       out resource,
                                                                       otherContainers: package.AfterResourcesInnerContainer ) )
        {
            return false;
        }
        _packageDescriptorContext.RegisterCodeHandledResources( resource );
        return true;
    }

    public bool RemoveCodeHandledResource( ResPackageDescriptor package, string resourceName, out ResourceLocator resource )
    {
        Throw.CheckArgument( "Package/Context mismatch.", package.Context == _packageDescriptorContext );
        if( package.ResourcesInnerContainer.TryGetResource( resourceName, out resource )
            || package.AfterResourcesInnerContainer.TryGetResource( resourceName, out resource ) )
        {
            _packageDescriptorContext.RegisterCodeHandledResources( resource );
            return true;
        }
        return false;
    }

    public IReadOnlyDictionary<object, ResPackageDescriptor> PackageIndex => _packageIndex;

    public IReadOnlyCollection<ResPackageDescriptor> Packages => _packages;

    public int LocalPackageCount => _localPackageCount;

    public int TypedPackageCount => _typedPackageCount;

    public int SingleMappingCount => _packageDescriptorContext.SingleMappingCount;

    public GlobalTypeCache TypeCache => _typeCache;

    void RemoveDefinitelyOptional( ResPackageDescriptor p )
    {
        Throw.DebugAssert( p.IsOptional );
        if( p.IsLocalPackage )
        {
            --_localPackageCount;
        }
        _packageIndex.Remove( p.FullName );
        _packageIndex.Remove( p.ResourcesInnerContainer );
        _packageIndex.Remove( p.AfterResourcesInnerContainer );
        if( p.Type != null )
        {
            --_typedPackageCount;
            _packageIndex.Remove( p.Type );
        }
    }

    bool SetNoMoreOptionalPackage( IActivityMonitor monitor, ResPackageDescriptor p )
    {
        Throw.DebugAssert( p.IsOptional );
        Throw.DebugAssert( !_packages.Contains( p ) );
        p._isOptional = false;
        _packages.Add( p );
        return EnsureAlsoRegistrations( monitor, p );
    }

    public bool Close( IActivityMonitor monitor,
                       out HashSet<ResourceLocator> codeHandledResources,
                       out IReadOnlyList<ResPackageDescriptor> finalOptionalPackages )
    {
        bool success = true;
        // First, we must handle the AlsoRegisterTypes of non optional packages
        // that are not already regsitered.
        // We do this here (in the Close) to let regular registrations "win" over registrations from
        // the AlsoRegisterTypes.
        foreach( ResPackageDescriptor p in _packages )
        {
            success &= EnsureAlsoRegistrations( monitor, p );
        }
        if( success )
        {
            var sorter = new TopologicalSorter( monitor, this );
            success = sorter.Run();
        }
        // Satisfy the out parameters even on failure.
        finalOptionalPackages = _optionalPackages;
        codeHandledResources = _packageDescriptorContext.Close();
        return success;
    }

    bool EnsureAlsoRegistrations( IActivityMonitor monitor, ResPackageDescriptor p )
    {
        Throw.DebugAssert( !p.IsOptional );
        // Skips manifest based packages. We will see if manifests also define "also register types"
        // when we actually support manifests. 
        bool success = true;
        if( p.Type != null )
        {
            foreach( var also in p.Type.AlsoRegisterTypes )
            {
                if( _packageIndex.TryGetValue( also, out var already ) )
                {
                    // If the also registered package is optional, it is no more optional.
                    if( already.IsOptional )
                    {
                        success &= SetNoMoreOptionalPackage( monitor, already );
                        monitor.Debug( $"{(already.IsGroup ? "Group" : "Package")} '{also}' is no more optional because of [AlsoRegisterType<{also.Name}>] on '{p.Type}'." );
                    }
                }
                else
                {
                    // The also registered type is not yet known: we must register it.
                    monitor.Debug( $"Also registering '{also}' because of [AlsoRegisterType<{also.Name}>] on '{p.Type}'." );
                    success &= this.RegisterPackage( monitor, also, isOptional: false ) != null;
                }
            }
        }
        return success;
    }
}
