using System.Collections.Immutable;
using System.Linq;

namespace CK.Core;

/// <summary>
/// Base class for file resource handlers.
/// </summary>
public abstract partial class ResourceSpaceFileHandler : IResourceSpaceHandler
{
    readonly IResourceSpaceItemInstaller? _installer;
    readonly ImmutableArray<string> _fileExtensions;

    /// <summary>
    /// Initializes a new handler for resources with the provided file extensions.
    /// </summary>
    /// <param name="installer">The installer that <see cref="Install(IActivityMonitor)"/> will use.</param>
    /// <param name="fileExtensions">One or more file extensions that must start with '.' (like ".css").</param>
    protected ResourceSpaceFileHandler( IResourceSpaceItemInstaller? installer, params ImmutableArray<string> fileExtensions )
    {
        Throw.CheckArgument( fileExtensions.Length > 0 && fileExtensions.All( e => e.Length >= 2 && e[0] == '.' ) );
        _installer = installer;
        _fileExtensions = fileExtensions;
    }

    /// <summary>
    /// Gets the file extensions that will be handled by this handler.
    /// </summary>
    public ImmutableArray<string> FileExtensions => _fileExtensions;


    /// <summary>
    /// Gets the configured installer that <see cref="Install(IActivityMonitor)"/> will use.
    /// </summary>
    public IResourceSpaceItemInstaller? Installer => _installer;

    /// <summary>
    /// Must initialize this handler.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="spaceData">The space data to consider.</param>
    /// <param name="folderFilter">The filter for resources or folder to use.</param>
    /// <returns>True on success, false on error. Errors must be logged.</returns>
    internal protected abstract bool Initialize( IActivityMonitor monitor,
                                                 ResCoreData spaceData,
                                                 FolderExclusion folderFilter );

    /// <summary>
    /// Called by <see cref="ResSpace.Install(IActivityMonitor)"/> (even if <see cref="Installer"/> is null).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false otherwise.</returns>
    internal protected abstract bool Install( IActivityMonitor monitor );

    /// <summary>
    /// Overridden to return the type of this handler and the extensions it handles.
    /// </summary>
    /// <returns>A readable string.</returns>
    public sealed override string ToString() => $"{GetType().Name} - Files '*{_fileExtensions.Concatenate( "', *'" )}'";

}
