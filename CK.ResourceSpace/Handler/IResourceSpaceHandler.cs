namespace CK.Core;

/// <summary>
/// Generalizes <see cref="ResourceSpaceFileHandler"/> and <see cref="ResourceSpaceFolderHandler"/>.
/// <para>
/// This is internal because it has no interest to be public. This interface supports the
/// lifetime of the <see cref="IResourceSpaceItemInstaller"/> instances that have been associated
/// to the handlers: once install succeeded or failed, the <see cref="IResourceSpaceItemInstaller.Close( IActivityMonitor, bool)"/>
/// is called on the distinct installers.
/// </para>
/// </summary>
internal interface IResourceSpaceHandler
{
    /// <summary>
    /// Gets the optional associated target installer.
    /// </summary>
    IResourceSpaceItemInstaller? Installer { get; }
}
