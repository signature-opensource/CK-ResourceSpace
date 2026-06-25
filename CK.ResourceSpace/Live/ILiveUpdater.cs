namespace CK.Core;

/// <summary>
/// Minimal Live interface that must handle defferred application of any number of changes.
/// </summary>
public interface ILiveUpdater
{
    /// <summary>
    /// Called when a path has changed. The file may have been removed. When <see cref="PathChangedEvent.SubPath"/>
    /// is empty, it means that a "global" change at the <see cref="IResPackageResources.LocalPath"/> folder
    /// level occurred.
    /// <para>
    /// This applies to <see cref="ResourceSpaceFolderHandler"/> and <see cref="ResourceSpaceFileHandler"/>, there is
    /// no <see cref="ResourceSpaceFileHandler.FolderExclusion"/> for files:
    /// <list type="bullet">
    ///     <item>The first updater that returns true ends the calls.</item>
    ///     <item>
    ///     Folder updaters are called first and must return true if the changed file is
    ///     in their <see cref="ResourceSpaceFolderHandler.RootFolderName"/> (even if they ignore it).
    ///     </item>
    ///     <item>
    ///     =&gt; File updaters are never called with a file that is inside a folder handled by a <see cref="ResourceSpaceFolderHandler"/>.
    ///     </item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="changed">The path changed event.</param>
    /// <returns>Must return true if the change is handled by this handler, false if the change doesn't concern this handler.</returns>
    bool OnChange( IActivityMonitor monitor, PathChangedEvent changed );

    /// <summary>
    /// Must apply all changes collected so far.
    /// Error management is up to the updater. The goal is to minimize the useless changes in the target folder
    /// and to clean up everything that should not exist anymore.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    void ApplyChanges( IActivityMonitor monitor );
}
