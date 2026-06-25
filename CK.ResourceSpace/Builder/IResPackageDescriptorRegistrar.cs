using CK.EmbeddedResources;
using CK.Engine.TypeCollector;
using System.Resources;

namespace CK.Core;

/// <summary>
/// This is the only way to create <see cref="ResPackageDescriptor"/> instances.
/// </summary>
public interface IResPackageDescriptorRegistrar
{
    /// <summary>
    /// Gets the global type cache.
    /// </summary>
    GlobalTypeCache TypeCache { get; }

    /// <summary>
    /// Finds a mutable package descriptor by its full name.
    /// </summary>
    /// <param name="fullName">The full name.</param>
    /// <returns>The package or null if not found.</returns>
    ResPackageDescriptor? FindByFullName( string fullName );

    /// <summary>
    /// Finds a mutable package descriptor by its type.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The package or null if not found.</returns>
    ResPackageDescriptor? FindByType( ICachedType type );

    /// <summary>
    /// Registers a package. It must not already exist: <paramref name="fullName"/>, <paramref name="resourceStore"/>
    /// or <paramref name="resourceAfterStore"/> must not have been already registered.
    /// <para>
    /// <paramref name="resourceStore"/> and <paramref name="resourceAfterStore"/> must be <see cref="IResourceContainer.IsValid"/>
    /// and must not be the same instance.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="fullName">The package full name.</param>
    /// <param name="defaultTargetPath">The default target path associated to the package's resources.</param>
    /// <param name="resourceStore">
    /// The package resources. Must be <see cref="IResourceContainer.IsValid"/>.
    /// </param>
    /// <param name="resourceAfterStore">
    /// The package resources to apply after the <see cref="ResPackageDescriptor.Children"/>.
    /// Must be <see cref="IResourceContainer.IsValid"/>.
    /// </param>
    /// <param name="isOptional">
    /// Defines the initial value of <see cref="ResPackageDescriptor.IsOptional"/>. By default, this
    /// is specified by the CKPackage manifest and if there is no CKPackage manifest, this is false.
    /// </param>
    /// <returns>The package descriptor on success, null on error.</returns>
    ResPackageDescriptor? RegisterPackage( IActivityMonitor monitor,
                                           string fullName,
                                           NormalizedPath defaultTargetPath,
                                           IResourceContainer resourceStore,
                                           IResourceContainer resourceAfterStore,
                                           bool? isOptional );

    /// <summary>
    /// Registers a package. It must not already exist: <paramref name="type"/> must not have been already registered.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="type">The type that defines the package.</param>
    /// <param name="defaultTargetPath">The default target path associated to the package's resources.</param>
    /// <param name="isOptional">
    /// Defines the initial value of <see cref="ResPackageDescriptor.IsOptional"/>. By default, this
    /// is driven by the existence and value of the <see cref="IOptionalResourceGroupAttribute.IsOptional" /> attributes.
    /// </param>
    /// <param name="ignoreLocal">
    /// True to ignore local folder: even if the resources are in a local folder, this creates
    /// a <see cref="AssemblyResourceContainer"/> instead of a <see cref="FileSystemResourceContainer"/>
    /// for both <see cref="ResPackageDescriptor.Resources"/> and <see cref="ResPackageDescriptor.AfterResources"/>.
    /// <para>
    /// This is mainly for tests.
    /// </para>
    /// </param>
    /// <returns>The package descriptor on success, null on error.</returns>
    ResPackageDescriptor? RegisterPackage( IActivityMonitor monitor,
                                           ICachedType type,
                                           NormalizedPath defaultTargetPath,
                                           bool? isOptional = null, 
                                           bool ignoreLocal = false );

    /// <summary>
    /// Removes a resource that must belong to the <paramref name="package"/>'s Resources or AfterResources
    /// from the package's resources (strictly speaking, the resource is "hidden").
    /// The same resource can be removed more than once.
    /// <para>
    /// This enable code generators to take control of a resource that they want to handle directly.
    /// The resource will no more appear in the package's resources.
    /// </para>
    /// <para>
    /// How the removed resource is "transferred" (or not) in the <see cref="ResSpaceCollector.GeneratedCodeContainer"/>
    /// is up to the code generators.
    /// </para>
    /// <para>
    /// Care should be taken if the package is optional (when <see cref="ResPackageDescriptor.IsOptional"/> is true):
    /// decisions such as transfering the resource to the &lt;Code&gt; container will be problematic if the
    /// package is not eventually required. When packages are optional, it is better to handle the resources on the
    /// <see cref="ResSpaceData"/> in which optional packages don't appear.
    /// </para>
    /// </summary>
    /// <param name="package">The package that contains the resource.</param>
    /// <param name="resource">The resource to remove from stores.</param>
    void RemoveCodeHandledResource( ResPackageDescriptor package, ResourceLocator resource );

    /// <summary>
    /// Finds the <paramref name="resourceName"/> that must exist in the <paramref name="package"/>'s Resources or AfterResources
    /// and calls <see cref="RemoveCodeHandledResource(ResPackageDescriptor, ResourceLocator)"/> or logs an error if the
    /// resource is not found.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="package">The package that contains the resource.</param>
    /// <param name="resourceName">The resource name to find.</param>
    /// <param name="resource">The found resource.</param>
    /// <returns>True on success, false if the resource cannot be found (an error is logged).</returns>
    bool RemoveExpectedCodeHandledResource( IActivityMonitor monitor,
                                            ResPackageDescriptor package,
                                            string resourceName,
                                            out ResourceLocator resource );

    /// <summary>
    /// Finds the <paramref name="resourceName"/> that may exist in <see cref="ResPackage.Resources"/> or <see cref="ResPackage.AfterResources"/>
    /// and calls <see cref="RemoveCodeHandledResource(ResPackageDescriptor, ResourceLocator)"/> if the resource is found.
    /// </summary>
    /// <param name="package">The package that contains the resource.</param>
    /// <param name="resourceName">The resource name to find.</param>
    /// <param name="resource">The found (and removed) resource.</param>
    /// <returns>True if the resource has been found and removed, false otherwise.</returns>
    bool RemoveCodeHandledResource( ResPackageDescriptor package, string resourceName, out ResourceLocator resource );
}
