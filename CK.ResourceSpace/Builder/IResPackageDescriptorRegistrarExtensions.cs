using CK.EmbeddedResources;
using CK.Engine.TypeCollector;
using System;

namespace CK.Core;

/// <summary>
/// Extends <see cref="IResPackageDescriptorRegistrar"/>.
/// </summary>
public static class IResPackageDescriptorRegistrarExtensions
{
    /// <summary>
    /// Registers a package. It must not already exist: the type nor its fullname must not have been already registered.
    /// <para>
    /// The <see cref="ResPackageDescriptor.DefaultTargetPath"/> is derived from the type's namespace:
    /// "The/Type/Namespace" (the dots of the namespace are replaced with a '/'.
    /// </para>
    /// </summary>
    /// <param name="r">This registrar.</param>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="type">The type that defines the package.</param>
    /// <param name="isOptional">
    /// Defines the initial value of <see cref="ResPackageDescriptor.IsOptional"/>. By default, this
    /// is driven by the existence and value of the <see cref="IOptionalResourceGroupAttribute" /> attributes.
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
    public static ResPackageDescriptor? RegisterPackage( this IResPackageDescriptorRegistrar r,
                                                         IActivityMonitor monitor,
                                                         ICachedType type,
                                                         bool? isOptional = null,
                                                         bool ignoreLocal = false )
    {
        Throw.CheckArgument( type.EngineUnhandledType is EngineUnhandledType.None );
        // Namespace is null for regular types not declared in a namespace.
        var targetPath = type.Type.Namespace?.Replace( '.', '/' ) ?? string.Empty;
        return r.RegisterPackage( monitor, type, targetPath, isOptional, ignoreLocal );
    }

    /// <inheritdoc cref="RegisterPackage(IResPackageDescriptorRegistrar, IActivityMonitor, ICachedType, bool?, bool)"/>
    public static ResPackageDescriptor? RegisterPackage( this IResPackageDescriptorRegistrar r,
                                                         IActivityMonitor monitor,
                                                         Type type,
                                                         bool? isOptional = null,
                                                         bool ignoreLocal = false )
    {
        return RegisterPackage( r, monitor, r.TypeCache.Get( type ), isOptional, ignoreLocal );
    }


}
