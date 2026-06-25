using CK.EmbeddedResources;
using CK.Engine.TypeCollector;
using System;
using System.Collections.Generic;

namespace CK.Core;

/// <summary>
/// Mutable package descriptor.
/// </summary>
public sealed partial class ResPackageDescriptor
{
    readonly ResPackageDescriptorContext _context;
    readonly string _fullName;
    readonly ICachedType? _type;
    readonly NormalizedPath _defaultTargetPath;
    readonly StoreContainer _resources;
    readonly StoreContainer _afterResources;
    List<object>? _singleMappings;
    // Accessed by the CoreCollector during topological sort.
    // The sorter sets this to null if only optional references were in it.
    internal List<Ref>? _requires;
    // The sorter sets this to null if only optional references were in it.
    internal List<Ref>? _children;
    // The sorter transfers RequiredBy, Groups and Package to Requires and Children. 
    internal List<Ref>? _requiredBy;
    internal List<Ref>? _groups;
    internal Ref _package;

    // Topological sort state. This is all that we need.
    //  - The sorted package index.
    internal int _idxPackage;
    //  - The sorted resources index.
    internal int _idxHeader;
    //  - The sorted after resources index.
    internal int _idxFooter;
    // - Set to true as soon as a group contains it or it is required by another package:
    //   When still false after the sort, this is an
    //   entry point that must be required by the <App>.
    internal bool _hasIncomingDeps;

    // The sorter may transition this from true to false.
    internal bool _isOptional;
    // IsGroup is mutable. The topological sort checks incoherencies
    // of Group vs. Package definition.
    bool _isGroup;

    internal ResPackageDescriptor( ResPackageDescriptorContext context,
                                   string fullName,
                                   ICachedType? type,
                                   NormalizedPath defaultTargetPath,
                                   StoreContainer resources,
                                   StoreContainer afterResources )
    {
        Throw.DebugAssert( resources != afterResources );
        _context = context;
        _fullName = fullName;
        _type = type;
        _defaultTargetPath = defaultTargetPath;
        _resources = resources;
        _afterResources = afterResources;
        _idxPackage = -1;
        _idxHeader = -1;
        _idxFooter = -1;
    }

    /// <summary>
    /// Gets this package full name. When built from a type, this is the type's full name.
    /// </summary>
    public string FullName => _fullName;

    /// <summary>
    /// Gets the type if this package is defined by a type.
    /// </summary>
    public ICachedType? Type => _type;

    /// <summary>
    /// Gets whether this is a local package: its <see cref="Resources"/> or <see cref="AfterResources"/>
    /// is a <see cref="FileSystemResourceContainer"/> with a true <see cref="FileSystemResourceContainer.HasLocalFilePathSupport"/>.
    /// </summary>
    public bool IsLocalPackage => _resources.LocalPath != null || _afterResources.LocalPath != null;

    /// <summary>
    /// Gets whether this package has been registered as an optional one.
    /// When this is still true after <see cref="ResSpaceDataBuilder.Build(IActivityMonitor)"/> has run,
    /// this package doesn't belong to the final <see cref="ResSpaceData"/>.
    /// </summary>
    public bool IsOptional => _isOptional;

    /// <summary>
    /// Gets the "Res" resources for this package.
    /// </summary>
    public IResourceContainer Resources => _resources;

    internal IResourceContainer ResourcesInnerContainer => _resources.InnerContainer;

    /// <summary>
    /// Gets the "Res[After]" resources for this package.
    /// They apply after this package's <see cref="Children"/>.
    /// </summary>
    public IResourceContainer AfterResources => _afterResources;

    internal IResourceContainer AfterResourcesInnerContainer => _afterResources.InnerContainer;

    /// <summary>
    /// Adds a mapping from a name to this package descriptor.
    /// The <paramref name="alias"/> must not be already associated to a package descriptor
    /// otherwise an error is logged and false is returned.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="alias">The name to map to this descriptor.</param>
    /// <returns>True on success, false on error.</returns>
    public bool AddSingleMapping( IActivityMonitor monitor, string alias )
    {
        ThrowOnClosedContext();
        Throw.CheckNotNullOrWhiteSpaceArgument( alias );
        if( _context.AddSingleMapping( monitor, alias, this ) )
        {
            _singleMappings ??= new List<object>();
            _singleMappings.Add( alias );
            return true;
        }
        return false;
    }

    /// <summary>
    /// Adds a mapping from a type to this package descriptor:
    /// <list type="number">
    ///     <item>This <see cref="Type"/> must not be null.</item>
    ///     <item>The <paramref name="alias"/> must be assignable from <see cref="Type"/>.</item>
    ///     <item>The <paramref name="alias"/> must not be already associated to a package descriptor.</item>
    /// </list>
    /// If any of these conditions is not met, an error is logged and false is returned.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="alias">The type to map to this descriptor.</param>
    /// <returns>True on success, false on error.</returns>
    public bool AddSingleMapping( IActivityMonitor monitor, ICachedType alias )
    {
        ThrowOnClosedContext();
        Throw.CheckNotNullArgument( alias );
        if( _context.AddSingleMapping( monitor, alias, this ) )
        {
            _singleMappings ??= new List<object>();
            _singleMappings.Add( alias );
            return true;
        }
        return false;
    }

    /// <inheritdoc cref="AddSingleMapping(IActivityMonitor, ICachedType)"/>
    public bool AddSingleMapping( IActivityMonitor monitor, Type alias ) => AddSingleMapping( monitor, _context.TypeCache.Get( alias ) );

    internal IReadOnlyList<object>? SingleMappings => _singleMappings;

    /// <summary>
    /// Gets the default target path that will prefix resources that are items.
    /// </summary>
    public NormalizedPath DefaultTargetPath => _defaultTargetPath;

    /// <summary>
    /// Gets or sets whether this is a group instead of a regular package.
    /// <para>
    /// Defaults to false. When registering a Type, this is set to true if the
    /// type doesn't support <see cref="IResourcePackage"/>.
    /// </para>
    /// </summary>
    public bool IsGroup
    {
        get => _isGroup;
        set
        {
            ThrowOnClosedContext();
            _isGroup = value;
        }
    }

    /// <summary>
    /// Gets or sets a <see cref="Ref"/> to the package that owns this one.
    /// <para>
    /// Defaults to the <c>default</c> value (<see cref="Ref.IsValid"/> is false): there
    /// is no owner.
    /// </para>
    /// <para>
    /// Before calling <see cref="ResSpaceDataBuilder.Build(IActivityMonitor)"/>, <see cref="IsGroup"/> must
    /// be false otherwise this will be an error.
    /// </para>
    /// </summary>
    public Ref Package
    {
        get => _package;
        set
        {
            ThrowOnClosedContext();
            _package = value;
        }
    }

    /// <summary>
    /// Gets a mutable list of requirements that can be optional references.
    /// </summary>
    public IList<Ref> Requires => _requires ??= new List<Ref>();

    /// <summary>
    /// Gets a mutable list of revert dependencies (a package can specify that it is itself required by another one). 
    /// Often, a "RequiredBy" constraint should be specified as <see cref="Ref.IsOptional"/>.
    /// </summary>
    public IList<Ref> RequiredBy => _requiredBy ??= new List<Ref>();

    /// <summary>
    /// Gets a mutable list of children.
    /// </summary>
    public IList<Ref> Children => _children ??= new List<Ref>();

    /// <summary>
    /// Gets a mutable list of groups to which this item belongs. If one of these groups is a package,
    /// it must be the only package of this item (otherwise it is an error).
    /// </summary>
    public IList<Ref> Groups => _groups ??= new List<Ref>();

    internal ResPackageDescriptorContext Context => _context;

    internal bool CheckContext( IActivityMonitor monitor, string relName, ResPackageDescriptorContext expected )
    {
        if( _context != expected )
        {
            monitor.Error( $"'{relName}' relationship context mismatch. The package '{_fullName}' belongs to another collector." );
            return false;
        }
        return true;
    }


    void ThrowOnClosedContext() => Throw.CheckState( "ResSpaceDataBuilder.Build() has been called.", Context.Closed is false );

    /// <inheritdoc cref="ResPackage.ToString()"/>
    public override string ToString() => _fullName;

}
