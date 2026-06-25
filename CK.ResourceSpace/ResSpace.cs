using CK.BinarySerialization;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;

namespace CK.Core;

/// <summary>
/// Final production initiated by a <see cref="ResSpaceConfiguration"/>.
/// Once available, all registered <see cref="FolderHandlers"/> and <see cref="FileHandlers"/>
/// have been successfully initialized.
/// </summary>
public sealed partial class ResSpace
{
    /// <summary>
    /// The Live state file name.
    /// </summary>
    public const string LiveStateFileName = "LiveState.dat";

    /// <summary>
    /// The current serialization format version.
    /// </summary>
    public static readonly byte CurrentVersion = 0;

    readonly ResCoreData _data;
    readonly ImmutableArray<ResourceSpaceFolderHandler> _folderHandlers;
    readonly ImmutableArray<ResourceSpaceFileHandler> _fileHandlers;
    readonly ResourceSpaceFileHandler.FolderExclusion _folderExclusion;
    
    internal ResSpace( ResCoreData data,
                       ImmutableArray<ResourceSpaceFolderHandler> folderHandlers,
                       ImmutableArray<ResourceSpaceFileHandler> fileHandlers )
    {
        _data = data;
        _folderHandlers = folderHandlers;
        _fileHandlers = fileHandlers;
        _folderExclusion = new ResourceSpaceFileHandler.FolderExclusion( folderHandlers );
    }

    /// <summary>
    /// Gets the resources data.
    /// </summary>
    public ResCoreData Data => _data;

    /// <summary>
    /// Gets the successfully initialized <see cref="ResourceSpaceFolderHandler"/>.
    /// </summary>
    public ImmutableArray<ResourceSpaceFolderHandler> FolderHandlers => _folderHandlers;

    /// <summary>
    /// Gets the successfully initialized <see cref="ResourceSpaceFileHandler"/>.
    /// </summary>
    public ImmutableArray<ResourceSpaceFileHandler> FileHandlers => _fileHandlers;

    // Called by the ResourceSpaceBuilder.Build().
    internal bool Initialize( IActivityMonitor monitor )
    {
        bool success = true;
        foreach( var h in _folderHandlers )
        {
            success &= h.Initialize( monitor, _data );
        }
        foreach( var h in _fileHandlers )
        {
            success &= h.Initialize( monitor, _data, _folderExclusion );
        }
        return success;
    }
        
    /// <summary>
    /// Installs resources.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false otherwise.</returns>
    public bool Install( IActivityMonitor monitor )
    {
        var installers = new HashSet<IResourceSpaceItemInstaller>();
        bool success = true;
        foreach( var h in _folderHandlers )
        {
            if( !(success = OpenInstaller( monitor, this, installers, h.Installer )) )
            {
                break;
            }
        }
        foreach( var h in _fileHandlers )
        {
            if( !(success = OpenInstaller( monitor, this, installers, h.Installer )) )
            {
                break;
            }
        }
        if( !success )
        {
            CloseInstallers( monitor, installers, success );
            return false;
        }
        if( _data.LiveStatePath != ResSpaceCollector.NoLiveState )
        {
            success &= ClearLiveState( monitor );
        }
        foreach( var f in _folderHandlers )
        {
            success &= f.Install( monitor );
        }
        foreach( var f in _fileHandlers )
        {
            success &= f.Install( monitor );
        }
        CloseInstallers( monitor, installers, success );

        if( success )
        {
            if( _data.WatchRoot == null )
            {
                var msg = _data.LiveStatePath == ResSpaceCollector.NoLiveState
                            ? "Live state is disabled."
                            : "No local package exist and no AppResourcesLocalPath has been set.";
                monitor.Warn( $"""
                               {msg}
                               Skipping Live state generation.
                               """ );
            }
            else
            {
                success &= WriteLiveState( monitor );
            }
        }
        return success;

        static bool OpenInstaller( IActivityMonitor monitor,
                                   ResSpace s,
                                   HashSet<IResourceSpaceItemInstaller> installers,
                                   IResourceSpaceItemInstaller? i )
        {
            if( i != null && installers.Add( i ) )
            {
                try
                {
                    if( !i.Open( monitor, s ) )
                    {
                        return false;
                    }
                }
                catch( Exception ex )
                {
                    monitor.Error( ex );
                    return false;
                }
            }
            return true;
        }

        static void CloseInstallers( IActivityMonitor monitor,
                                     HashSet<IResourceSpaceItemInstaller> installers,
                                     bool success )
        {
            foreach( var i in installers )
            {
                try
                {
                    i.Close( monitor, success );
                }
                catch( Exception ex )
                {
                    monitor.Error( $"While closing '{i}'.", ex );
                }
            }
        }

    }

    bool ClearLiveState( IActivityMonitor monitor )
    {
        Throw.DebugAssert( _data.LiveStatePath.EndsWith( Path.DirectorySeparatorChar ) );
        var stateFile = _data.LiveStatePath + LiveStateFileName;
        if( File.Exists( stateFile ) )
        {
            int retryCount = 0;
            retry:
            try
            {
                File.Delete( stateFile );
                monitor.Trace( $"Deleted state file '{stateFile}'." );
            }
            catch( Exception ex )
            {
                if( ++retryCount < 3 )
                {
                    monitor.Warn( $"While clearing state file '{stateFile}'.", ex );
                    Thread.Sleep( retryCount * 100 );
                    goto retry;
                }
                monitor.Warn( $"Unable to delete state file '{stateFile}'.", ex );
            }
        }
        return true;
    }

    bool WriteLiveState( IActivityMonitor monitor )
    {
        var ckWatchFolderPath = _data.LiveStatePath;
        if( _data.WatchRoot == null )
        {
            var msg = ckWatchFolderPath == ResSpaceCollector.NoLiveState
                        ? "Live state is disabled."
                        : "No local package exist and no AppResourcesLocalPath has been set.";
            monitor.Info( $"""
                          {msg}
                          Skipping Live state generation.
                          """ );
            return true;
        }
        var liveHandlers = _folderHandlers.OfType<ILiveResourceSpaceHandler>()
                                          .Concat( _fileHandlers.OfType<ILiveResourceSpaceHandler>() )
                                          .Where( h => !h.DisableLiveUpdate )
                                          .ToList();
        if( liveHandlers.Count == 0 )
        {
            monitor.Info( $"""
                No enabled Live resource handlers exist in the {_folderHandlers.Length} folders and {_fileHandlers.Length} files resource handlers.
                Skipping Live state generation.
                """ );
            return true;
        }
        using var _ = monitor.OpenInfo( $"Saving ck-watch live state. Watch root is '{_data.WatchRoot}'." );
        if( !Directory.Exists( ckWatchFolderPath ) )
        {
            monitor.Info( $"Creating '{ckWatchFolderPath}' with '.gitignore' all." );
            Directory.CreateDirectory( ckWatchFolderPath );
            File.WriteAllText( ckWatchFolderPath + ".gitignore", "*" );
        }
        // Don't store the BinarySerializerContext for reuse here: this is a one-shot.
        bool success = true;
        using( var streamLive = new FileStream( ckWatchFolderPath + LiveStateFileName, FileMode.Create ) )
        using( var s = BinarySerializer.Create( streamLive, new BinarySerializerContext() ) )
        {
            s.Writer.Write( CurrentVersion );
            s.WriteObject( _data );
            s.Writer.WriteNonNegativeSmallInt32( liveHandlers.Count );
            foreach( var h in liveHandlers )
            {
                s.WriteTypeInfo( h.GetType() );
                success &= h.WriteLiveState( monitor, s, _data );
            }
        }
        return success;
    }
}
