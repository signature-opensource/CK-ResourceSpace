using CK.EmbeddedResources;
using System.Collections.Generic;

namespace CK.Core;

/// <summary>
/// Models the <see cref="ResPackage.Resources"/> and <see cref="ResPackage.AfterResources"/>.
/// </summary>
public interface IResPackageResources
{
    /// <summary>
    /// Gets whether these resources are the <see cref="ResPackage.AfterResources"/> (or the <see cref="ResPackage.Resources"/>).
    /// </summary>
    bool IsAfter { get; }

    /// <summary>
    /// Gets the index of this package resources in the <see cref="ResCoreData.AllPackageResources"/>.
    /// </summary>
    int Index { get; }

    /// <summary>
    /// Gets the index of this package resources in the <see cref="ResCoreData.LocalPackageResources"/>
    /// if this <see cref="LocalPath"/> is not null, -1 otherwise.
    /// </summary>
    int LocalIndex { get; }

    /// <summary>
    /// Gets whether this is the "&lt;Code&gt;" resource (<see cref="IsAfter"/> is true).
    /// </summary>
    bool IsCodeResources { get; }

    /// <summary>
    /// Gets whether this is the "&lt;App&gt;" resource (<see cref="IsAfter"/> is false).
    /// </summary>
    bool IsAppResources { get; }

    /// <summary>
    /// Gets the resources.
    /// </summary>
    IResourceContainer Resources { get; }

    /// <summary>
    /// Gets the package that defines these resources.
    /// </summary>
    ResPackage Package { get; }

    /// <summary>
    /// Gets the reachable packages from these resources: it is this <see cref="Package"/>'s <see cref="ResPackage.AfterReachables"/>
    /// if <see cref="IsAfter"/> is true, or this Package's <see cref="ResPackage.Reachables"/>.
    /// </summary>
    IReadOnlySet<ResPackage> Reachables { get; }

    /// <summary>
    /// Gets the local folder path if the <see cref="Resources"/> are in a <see cref="FileSystemResourceContainer"/>
    /// with a true <see cref="FileSystemResourceContainer.HasLocalFilePathSupport"/>.
    /// </summary>
    string? LocalPath { get; }
}

