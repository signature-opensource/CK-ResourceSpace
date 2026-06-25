namespace CK.Core;

/// <summary>
/// Extends <see cref="IResourceSpaceItemInstaller"/> to handle suppression.
/// </summary>
public interface ILiveResourceSpaceItemInstaller : IResourceSpaceItemInstaller
{
    /// <summary>
    /// Deletes the item.
    /// Unlike the regular <see cref="IResourceSpaceItemInstaller"/> methods that either succeed or throw,
    /// this may fail, log error, and kindly return false. Errors may be ignored (this depends on the actual
    /// type of the installation target).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="path">The item path to delete.</param>
    bool SafeDelete( IActivityMonitor monitor, NormalizedPath path );
}
