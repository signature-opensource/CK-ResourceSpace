using CK.BinarySerialization;
using System;
using System.IO;

namespace CK.Core;

public partial class AssetsResourceHandler : ILiveResourceSpaceHandler
{
    /// <summary>
    /// Live update is currently supported only when installing on the file system:
    /// the <see cref="FileSystemInstaller.TargetPath"/> is serialized in the live state
    /// and a basic <see cref="FileSystemInstaller"/> is deserialized to be the installer
    /// on the live side.
    /// </summary>
    public bool DisableLiveUpdate => Installer is not FileSystemInstaller;

    bool ILiveResourceSpaceHandler.WriteLiveState( IActivityMonitor monitor, IBinarySerializer s, ResCoreData spaceData )
    {
        Throw.DebugAssert( "Otherwise LiveState would have been disabled.", Installer is FileSystemInstaller );
        s.Writer.Write( ((FileSystemInstaller)Installer).TargetPath );
        s.Writer.Write( RootFolderName );
        return true;
    }

    /// <summary>
    /// Restores a <see cref="ILiveUpdater"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="spaceData">The deserialized resource space data.</param>
    /// <param name="d">The deserializer for the primary <see cref="ResSpace.LiveStateFileName"/>.</param>
    /// <returns>The live updater on success, null on error. Errors are logged.</returns>
    public static ILiveUpdater? ReadLiveState( IActivityMonitor monitor, ResCoreData spaceData, IBinaryDeserializer d )
    {
        var targetInstallPath = d.Reader.ReadString();
        var rootFolderName = d.Reader.ReadString();
        // No installer on the handler.
        // We reuse its logic only, not its Install capability.
        var handler = new AssetsResourceHandler( null, spaceData.SpaceDataCache, rootFolderName );
        // Our live installer knowns the "/assets/" sub folder.
        var installer = new FileSystemInstaller( targetInstallPath + rootFolderName + Path.DirectorySeparatorChar );
        return new LiveUpdater( handler, installer, spaceData );
    }


    sealed class LiveUpdater : ILiveUpdater
    {
        readonly AssetsResourceHandler _handler;
        readonly FileSystemInstaller _installer;
        readonly ResCoreData _data;
        bool _hasChanged;

        public LiveUpdater( AssetsResourceHandler handler, FileSystemInstaller installer, ResCoreData data )
        {
            _handler = handler;
            _installer = installer;
            _data = data;
        }

        public bool OnChange( IActivityMonitor monitor, PathChangedEvent changed )
        {
            if( !IsFileInRootFolder( _handler.RootFolderName, changed.SubPath, out ReadOnlySpan<char> localFile ) )
            {
                return false;
            }
            _hasChanged = true;
            _handler._cache.InvalidateCache( monitor, changed.Resources );
            return true;
        }

        public void ApplyChanges( IActivityMonitor monitor )
        {
            if( _hasChanged )
            {
                _hasChanged = false;
                var f = _handler.GetUnambiguousFinalAssets( monitor, _data );
                if( f != null ) WriteFinal( monitor, f, _installer );
            }
        }
    }
}
