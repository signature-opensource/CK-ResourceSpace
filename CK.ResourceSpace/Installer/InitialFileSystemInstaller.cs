using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CK.Core;

/// <summary>
/// Specialized <see cref="FileSystemInstaller"/> that tracks written files
/// and can cleanup any previously existing files.
/// <para>
/// Deferred files cleanup minimizes impacts on file watchers: we don't destroy/recreate the
/// target folder. Instead we update the existing files in place and then remove any files that
/// have not been generated.
/// </para>
/// </summary>
public sealed class InitialFileSystemInstaller : FileSystemInstaller
{
    readonly HashSet<string> _existing;

    /// <summary>
    /// Initializes a new <see cref="InitialFileSystemInstaller"/>.
    /// </summary>
    /// <param name="targetPath">The target folder.</param>
    public InitialFileSystemInstaller( string targetPath )
        : base( targetPath )
    {
        _existing = new HashSet<string>();
    }

    /// <summary>
    /// Ensures that the <see cref="FileSystemInstaller.TargetPath"/> exists and collects
    /// all existing files in it.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="resSpace">The resource space.</param>
    /// <returns>True on success, false on error.</returns>
    public override bool Open( IActivityMonitor monitor, ResSpace resSpace )
    {
        if( !base.Open( monitor, resSpace ) )
        {
            return false;
        }
        try
        {
            _existing.AddRange( Directory.EnumerateFiles( TargetPath, "*", SearchOption.AllDirectories ) );
            _existing.Remove( ".gitignore" );
            monitor.Info( $"Code generated target path contains {_existing.Count} files that will be updated." );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While collecting existing files in code generated target path: {TargetPath}", ex );
            return false;
        }
    }

    /// <summary>
    /// Removes the path from the collected existing paths.
    /// </summary>
    /// <param name="path">The file path that is installed.</param>
    protected override void OnWrite( string path ) => _existing.Remove( path );

    /// <summary>
    /// Handles files that has not been installed: on <paramref name="success"/>, they are deleted.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="success">True on success, false if an error occurred.</param>
    public override void Close( IActivityMonitor monitor, bool success )
    {
        if( _existing.Count == 0 )
        {
            monitor.Info( "No previous file exist that have not been regenerated. Nothing to delete." );
        }
        else
        {
            if( success )
            {
                using( monitor.OpenInfo( $"Deleting {_existing.Count} previous files." ) )
                {
                    foreach( var p in _existing )
                    {
                        monitor.Debug( $"Deleting '{p.AsSpan( TargetPath.Length )}'." );
                        try
                        {
                            if( File.Exists( p ) ) File.Delete( p );
                        }
                        catch( Exception ex )
                        {
                            monitor.Error( $"While deleting '{p}'. Ignoring.", ex );
                        }
                    }
                }
            }
            else
            {
                monitor.Info( $"""
                                Skipping deletion of {_existing.Count} previous files:
                                {_existing.Order().Concatenate( " > " + Environment.NewLine )}They may be deleted on the the next successful run.
                                """ );
            }
        }
    }
}
