using CK.EmbeddedResources;
using CK.Engine.TypeCollector;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CK.Core;

/// <summary>
/// Immutable resource package available in a <see cref="ResCoreData"/>.
/// </summary>
public sealed partial class ResPackage
{
    readonly ResCoreData _coreData;
    readonly string _fullName;
    readonly ICachedType? _type;
    readonly NormalizedPath _defaultTargetPath;
    readonly ImmutableArray<ResPackage> _requires;
    readonly AggregateId _requiresAggregateId;
    readonly ImmutableArray<ResPackage> _children;
    readonly AggregateId _childrenAggregateId;
    readonly IReadOnlySet<ResPackage> _reachables;
    readonly IReadOnlySet<ResPackage> _afterReachables;

    readonly ResBefore _resources;
    readonly ResAfter _afterResources;
    readonly int _index;
    readonly bool _isGroup;

    internal ResPackage( ResCoreDataCacheBuilder dataCacheBuilder,
                         string fullName,
                         NormalizedPath defaultTargetPath,
                         int idxBeforeResources,
                         IResourceContainer beforeResources,
                         int idxAfterResources,
                         IResourceContainer afterResources,
                         bool isGroup,
                         ICachedType? type,
                         ImmutableArray<ResPackage> requires,
                         ImmutableArray<ResPackage> children,
                         int index )
    {
        Throw.DebugAssert( "The <Code> package is the first one.", (index == 0) == (fullName == "<Code>") );
        Throw.DebugAssert( "The <App> package is the last one.", (index == dataCacheBuilder.TotalPackageCount - 1) == (fullName == "<App>") );

        Throw.DebugAssert( "The <Code> BeforeResources is empty by design.",
                           idxBeforeResources != 0
                           || (beforeResources is EmptyResourceContainer emptyCode && emptyCode.IsDisabled) );
        Throw.DebugAssert( "The <Code> AfterResources can be any IResourceContainer (if it has been set before ResSpaceDataBuilder.Build) or a ResourceContainerWrapper.",
                           idxAfterResources != 1
                           || beforeResources is IResourceContainer );

        Throw.DebugAssert( "Regular packages Before/AfterResources are StoreContainer.",
                           (fullName == "<Code>" || fullName == "<App>")
                           || (beforeResources is StoreContainer && afterResources is StoreContainer) );

        Throw.DebugAssert( "The <App> BeforeResources is a FileSystemResourceContainer or a EmptyResourceContainer (not disabled by design, " +
                            "it is disabled because there's no FileSystemResourceContainer).",
                           idxBeforeResources != (dataCacheBuilder.TotalPackageCount * 2) - 2
                           || (beforeResources is EmptyResourceContainer emptyAppBefore && !emptyAppBefore.IsDisabled)
                           || beforeResources is FileSystemResourceContainer );
        Throw.DebugAssert( "The <App> AfterResources is empty by design.",
                           idxAfterResources != (dataCacheBuilder.TotalPackageCount * 2) - 1
                           || (afterResources is EmptyResourceContainer emptyAppAfter && emptyAppAfter.IsDisabled) );

        _fullName = fullName;
        _defaultTargetPath = defaultTargetPath;
        _isGroup = isGroup;
        _index = index;
        _requires = requires;
        _children = children;
        _type = type;
        _coreData = dataCacheBuilder.SpaceData;
        // Initializes the resources.
        _resources = new ResBefore( this, beforeResources, idxBeforeResources );
        _afterResources = new ResAfter( this, afterResources, idxAfterResources );

        if( _requires.Length == 0 )
        {
            _reachables = ImmutableHashSet<ResPackage>.Empty;
            Throw.DebugAssert( _requiresAggregateId == default );
        }
        else
        {
            _reachables = dataCacheBuilder.GetReachableClosure( _requires, out _requiresAggregateId );
            Throw.DebugAssert( _requiresAggregateId != default
                               && _requiresAggregateId.HasLocal == _reachables.Any( r => r.IsEventuallyLocalDependent ) );
        }
        Throw.DebugAssert( "Reachables and Children don't overlap.", !_reachables.Overlaps( children ) );

        if( children.Length == 0 )
        {
            _afterReachables = _reachables;
            Throw.DebugAssert( _childrenAggregateId == default );
        }
        else
        {
            _childrenAggregateId = dataCacheBuilder.RegisterAggregate( _children );
            var closure = new HashSet<ResPackage>( _reachables );
            foreach( var c in _children )
            {
                if( closure.Add( c ) )
                {
                    closure.UnionWith( c._afterReachables );
                }
            }
            _afterReachables = closure;
            Throw.DebugAssert( _childrenAggregateId != default
                              && _childrenAggregateId.HasLocal == _children.SelectMany( c => c.AfterReachables ).Concat( _children )
                                                                           .Any( r => r.IsEventuallyLocalDependent ) );
        }
        Throw.DebugAssert( _afterReachables == _reachables || _afterReachables.IsProperSupersetOf( _reachables ) );
    }

    internal (AggregateId RequiresAggregateId, AggregateId ChildrenAggregateId) GetAggregateIdentifiers() => (_requiresAggregateId, _childrenAggregateId);

    /// <summary>
    /// Gets the <see cref="ResCoreData"/> that contains this package.
    /// </summary>
    public ResCoreData CoreData => _coreData;

    /// <summary>
    /// Gets this package full name. When built from a type, this is the type's full name.
    /// </summary>
    public string FullName => _fullName;

    /// <summary>
    /// Gets the default target path that will prefix resources that are items.
    /// </summary>
    public NormalizedPath DefaultTargetPath => _defaultTargetPath;

    /// <summary>
    /// Gets the <see cref="IResPackageResources"/> for this package.
    /// </summary>
    public IResPackageResources Resources => _resources;

    /// <summary>
    /// Gets the <see cref="IResPackageResources"/> that apply after this these <see cref="Resources"/>
    /// and package's <see cref="Children"/>.
    /// </summary>
    public IResPackageResources AfterResources => _afterResources;

    /// <summary>
    /// Gets whether this is a locally available package: either <see cref="Resources"/> or <see cref="AfterResources"/>
    /// has a non null <see cref="IResPackageResources.LocalPath"/>.
    /// </summary>
    public bool IsLocalPackage => _resources.LocalPath != null || _afterResources.LocalPath != null;

    /// <summary>
    /// Gets the type if this package is defined by a type.
    /// </summary>
    public ICachedType? Type => _type;

    /// <summary>
    /// Gets whether this package is a group.
    /// </summary>
    public bool IsGroup => _isGroup;

    /// <summary>
    /// Gets the index in the <see cref="ResCoreData.Packages"/>.
    /// </summary>
    public int Index => _index;

    /// <summary>
    /// Gets whether this is the &lt;Code&gt; package (the first <see cref="ResCoreData.Packages"/>).
    /// </summary>
    public bool IsCodePackage => _index == 0;

    /// <summary>
    /// Gets whether this is the &lt;App&gt; package (the last <see cref="ResCoreData.Packages"/>).
    /// </summary>
    public bool IsAppPackage => _index == _coreData._packages.Length - 1;

    /// <summary>
    /// Gets the direct set of packages that this package requires.
    /// <para>
    /// A child package is not a requirement and a requirement cannot be a child.
    /// </para>
    /// </summary>
    public ImmutableArray<ResPackage> Requires => _requires;

    /// <summary>
    /// Gets the set of packages that this package contains.
    /// <para>
    /// A child package is not a requirement and a requirement cannot be a child.
    /// </para>
    /// </summary>
    public ImmutableArray<ResPackage> Children => _children;

    /// <summary>
    /// Gets the packages that are reachable from this one: this is
    /// the closure of this <see cref="Requires"/> and their <see cref="Reachables"/>.
    /// <para>
    /// This excludes the children of this package, this is the point of view of the "head"
    /// of the package.
    /// </para>
    /// <para>
    /// The <see cref="AfterReachables"/> is the same set but from
    /// the point of view of the "tail" of the package that includes the <see cref="Reachables"/>
    /// of the <see cref="Children"/>.
    /// </para>
    /// </summary>
    public IReadOnlySet<ResPackage> Reachables => _reachables;

    /// <summary>
    /// Gets the packages that are reachable from the "tail" of this package:
    /// this extends <see cref="Reachables"/> with the <see cref="Children"/> and their <see cref="Reachables"/>.
    /// </summary>
    public IReadOnlySet<ResPackage> AfterReachables => _afterReachables;

    /// <summary>
    /// Gets whether this <see cref="IsLocalPackage"/> is true or one of the <see cref="AfterReachables"/>
    /// is local.
    /// </summary>
    public bool IsEventuallyLocalDependent => _requiresAggregateId.HasLocal || _childrenAggregateId.HasLocal || IsLocalPackage;

    /// <summary>
    /// Gets the <see cref="FullName"/>.
    /// </summary>
    /// <returns>The package full name.</returns>
    public override string ToString() => _fullName;
}
