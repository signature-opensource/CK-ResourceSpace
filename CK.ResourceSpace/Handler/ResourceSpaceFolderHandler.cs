using System;
using System.IO;

namespace CK.Core;

/// <summary>
/// Base class for folder resource handlers.
/// </summary>
public abstract class ResourceSpaceFolderHandler : IResourceSpaceHandler
{
    readonly IResourceSpaceItemInstaller? _installer;
    readonly string _rootFolderName;

    /// <summary>
    /// Initializes a new handler that will handle resources in the provided root folder.
    /// </summary>
    /// <param name="installer">The target installer to use.</param>
    /// <param name="rootFolderName">Must not be empty, whitespace and there must be no '/' or '\' in it.</param>
    protected ResourceSpaceFolderHandler( IResourceSpaceItemInstaller? installer, string rootFolderName )
    {
        Throw.CheckArgument( !string.IsNullOrWhiteSpace( rootFolderName ) && !rootFolderName.AsSpan().ContainsAny( "\\/" ) );
        _installer = installer;
        _rootFolderName = rootFolderName;
    }

    /// <summary>
    /// Gets the root folder name.
    /// Never empty nor whitespace and doesn't contain '/' or '\'.
    /// </summary>
    public string RootFolderName => _rootFolderName;

    /// <summary>
    /// Gets the configured installer that <see cref="Install(IActivityMonitor)"/> will use.
    /// </summary>
    public IResourceSpaceItemInstaller? Installer => _installer;

    /// <summary>
    /// Must initialize this handler.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="spaceData">The space data to consider.</param>
    /// <returns>True on success, false on error. Errors must be logged.</returns>
    internal protected abstract bool Initialize( IActivityMonitor monitor, ResCoreData spaceData );

    /// <summary>
    /// Called by <see cref="ResSpace.Install(IActivityMonitor)"/> (even if <see cref="Installer"/> is null).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false otherwise.</returns>
    internal protected abstract bool Install( IActivityMonitor monitor );

    /// <summary>
    /// Returns this handler's type name and <see cref="RootFolderName"/>.
    /// </summary>
    /// <returns>A readable string.</returns>
    public sealed override string ToString() => $"{GetType().Name} - Folder '{_rootFolderName}/'";

    /// <summary>
    /// Helper available to all <see cref="ILiveUpdater.OnChange(IActivityMonitor, PathChangedEvent)"/>.
    /// </summary>
    /// <param name="rootFolderName">The folder's handler <see cref="RootFolderName"/>.</param>
    /// <param name="filePath">The changed file path (the <see cref="PathChangedEvent.SubPath"/>).</param>
    /// <param name="localFilePath">
    /// The local file path (without the <paramref name="rootFolderName"/>).
    /// Can be empty or starts with the <see cref="Path.DirectorySeparatorChar"/>.
    /// </param>
    /// <returns>True if this file should be considered, false otherwise.</returns>
    public static bool IsFileInRootFolder( string rootFolderName, ReadOnlySpan<char> filePath, out ReadOnlySpan<char> localFilePath )
    {
        int lenRoot = rootFolderName.Length;
        int remainder = filePath.Length - lenRoot;
        if( remainder >= 0 )
        {
            if( filePath.StartsWith( rootFolderName ) )
            {
                if( remainder == 0 )
                {
                    localFilePath = default;
                    return true;
                }
                if( filePath[lenRoot] == Path.DirectorySeparatorChar )
                {
                    localFilePath = filePath.Slice( lenRoot );
                    return true;
                }
            }
        }
        localFilePath = default;
        return false;
    }

}
