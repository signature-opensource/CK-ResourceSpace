using CK.EmbeddedResources;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace CK.Core;

/// <summary>
/// <see cref="ResSpace"/> data is produced by the <see cref="ResSpaceDataBuilder"/>.
/// It contains the final, definitive set of <see cref="ResPackage"/> topologically ordered
/// but still allows resources to be altered.
/// <para>
/// This can be consumed by a <see cref="ResSpaceBuilder"/> that is configured with resource handlers
/// to eventually produce a <see cref="ResSpace"/>.
/// </para>
/// </summary>
public sealed class ResSpaceData
{
    readonly ResCoreData _coreData;
    readonly IReadOnlyList<ResPackageDescriptor> _finalOptionalPackages;
    HashSet<ResourceLocator>? _codeHandledResources;

    internal ResSpaceData( ResCoreData data,
                           HashSet<ResourceLocator> codeHandledResources,
                           IReadOnlyList<ResPackageDescriptor> finalOptionalPackages )
    {
        _coreData = data;
        _codeHandledResources = codeHandledResources;
        _finalOptionalPackages = finalOptionalPackages;
    }

    /// <summary>
    /// Gets the core data that contains the <see cref="ResPackage"/>.
    /// </summary>
    public ResCoreData CoreData => _coreData;

    /// <summary>
    /// Gets the <see cref="ResPackageDescriptor"/> that are eventually optional:
    /// no <see cref="ResPackage"/> has been created for them.
    /// </summary>
    public IReadOnlyList<ResPackageDescriptor> FinalOptionalPackages => _finalOptionalPackages;

    /// <summary>
    /// Gets or sets the configured Code generated resource container.
    /// This can only be set if this has not been previously set (ie. this is null).
    /// See <see cref="ResSpaceConfiguration.GeneratedCodeContainer"/>.
    /// </summary>
    [DisallowNull]
    public IResourceContainer? GeneratedCodeContainer
    {
        get => _coreData._generatedCodeContainer;
        set
        {
            Throw.CheckNotNullArgument( value );
            Throw.CheckState( "This can be set only once.", GeneratedCodeContainer is null );
            _coreData._generatedCodeContainer = value;
            // Ignore auto assignation: this just lock the container
            // (If the value is another ResourceContainerWrapper, this will throw and it's fine: we
            // don't want cycles!)
            if( _coreData._codePackage.AfterResources.Resources != value )
            {
                ((ResourceContainerWrapper)_coreData._codePackage.AfterResources.Resources).InnerContainer = value;
            }
        }
    }

    internal void Close()
    {
        Throw.DebugAssert( _codeHandledResources != null );
        _codeHandledResources = null;
    }

    [MemberNotNull( nameof( _codeHandledResources ) )]
    void ThrowOnClosed()
    {
        Throw.CheckState( "ResSpaceBuilder.Build() has been called.", _codeHandledResources != null );
    }

    /// <summary>
    /// Removes a resource that must belong to the <paramref name="package"/>'s Resources or AfterResources
    /// from the package's resources (strictly speaking, the resource is "hidden").
    /// The same resource can be removed more than once.
    /// <para>
    /// This enable code generators to take control of a resource that they want to handle directly.
    /// The resource will no more appear in the package's resources.
    /// </para>
    /// <para>
    /// How the removed resource is "transferred" (or not) in the <see cref="GeneratedCodeContainer"/>
    /// is up to the code generators.
    /// </para>
    /// </summary>
    /// <param name="package">The package that contains the resource.</param>
    /// <param name="resource">The resource to remove from stores.</param>
    public void RemoveCodeHandledResource( ResPackage package, ResourceLocator resource )
    {
        Throw.CheckArgument( "Package/Context mismatch.", package.CoreData == CoreData );
        Throw.CheckArgument( "Resource/Package mismatch.", resource.Container == package.Resources || resource.Container == package.AfterResources );
        ThrowOnClosed();
        if( package.IsCodePackage || package.IsAppPackage ) ThrowAppCodeArgumentException( package, out _ );
        _codeHandledResources.Add( resource );
    }

    /// <summary>
    /// Finds the <paramref name="resourceName"/> that must exist in the <paramref name="package"/>'s Resources or AfterResources
    /// and calls <see cref="RemoveCodeHandledResource(ResPackage, ResourceLocator)"/> or logs an error if the
    /// resource is not found.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="package">The package that contains the resource.</param>
    /// <param name="resourceName">The resource name to find.</param>
    /// <param name="resource">The found resource.</param>
    /// <returns>True on success, false if the resource cannot be found (an error is logged).</returns>
    public bool RemoveExpectedCodeHandledResource( IActivityMonitor monitor,
                                                   ResPackage package,
                                                   string resourceName,
                                                   out ResourceLocator resource )
    {
        Throw.CheckArgument( "Package/Context mismatch.", package.CoreData == CoreData );
        ThrowOnClosed();
        // Use the inner containers to find an already removed package and return true. 
        if( package.Resources.Resources is StoreContainer before
            && package.AfterResources.Resources is StoreContainer after )
        {
            if( !before.InnerContainer.TryGetExpectedResource( monitor,
                                                               resourceName,
                                                               out resource,
                                                               otherContainers: after.InnerContainer ) )
            {
                return false;
            }
            _codeHandledResources.Add( resource );
            return true;
        }
        return ThrowAppCodeArgumentException( package, out resource );
    }

    /// <summary>
    /// Finds the <paramref name="resourceName"/> that may exist in <see cref="ResPackage.Resources"/> or <see cref="ResPackage.AfterResources"/>
    /// and calls <see cref="RemoveCodeHandledResource(ResPackage, ResourceLocator)"/> if the resource is found.
    /// </summary>
    /// <param name="package">The package that contains the resource.</param>
    /// <param name="resourceName">The resource name to find.</param>
    /// <param name="resource">The found (and removed) resource.</param>
    /// <returns>True if the resource has been found and removed, false otherwise.</returns>
    public bool RemoveCodeHandledResource( ResPackage package, string resourceName, out ResourceLocator resource )
    {
        Throw.CheckArgument( "Package/Context mismatch.", package.CoreData == CoreData );
        ThrowOnClosed();
        // Use the inner containers to find an already removed package and return true. 
        if( package.Resources.Resources is StoreContainer before
            && package.AfterResources.Resources is StoreContainer after )
        {
            if( before.InnerContainer.TryGetResource( resourceName, out resource )
               || after.InnerContainer.TryGetResource( resourceName, out resource ) )
            {
                _codeHandledResources.Add( resource );
                return true;
            }
            return false;
        }
        return ThrowAppCodeArgumentException( package, out resource );

    }

    static bool ThrowAppCodeArgumentException( ResPackage package, out ResourceLocator resource, [CallerMemberName] string? callerName = null )
    {
        Throw.DebugAssert( package.IsCodePackage || package.IsAppPackage );
        throw new ArgumentException( $"{callerName} cannot be called on the <App> or <Code> package.", nameof( package ) );
    }

}

