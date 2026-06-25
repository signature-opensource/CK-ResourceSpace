using CK.EmbeddedResources;
using System;
using System.IO;
using System.Threading;

namespace CK.Core;

/// <summary>
/// Basic implementation that can be specialized.
/// </summary>
public class FileSystemInstaller : ILiveResourceSpaceItemInstaller
{
    readonly char[] _pathBuffer;
    string _targetPath;

    /// <summary>
    /// Initialize a new installer on a target path.
    /// The path must be fully qualified. It is normalized to end with <see cref="Path.DirectorySeparatorChar"/>.
    /// </summary>
    /// <param name="targetPath">The target folder.</param>
    public FileSystemInstaller( string targetPath )
    {
        Throw.CheckArgument( Path.IsPathFullyQualified( targetPath ) );
        targetPath = Path.GetFullPath( targetPath );
        if( targetPath[^1] != Path.DirectorySeparatorChar )
        {
            targetPath += Path.DirectorySeparatorChar;
        }
        _targetPath = targetPath;
        _pathBuffer = new char[1024];
        targetPath.CopyTo( _pathBuffer );
    }

    /// <summary>
    /// See <see cref="IResourceSpaceItemInstaller.Open(IActivityMonitor, ResSpace)"/>.
    /// Ensures that the <see cref="TargetPath"/> exists by creating it if needed.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="resSpace">The resource space to install.</param>
    /// <returns>True on sucess, false on error.</returns>
    public virtual bool Open( IActivityMonitor monitor, ResSpace resSpace )
    {
        return CheckLiveStateCoherency( monitor, resSpace.Data )
               && EnsureTargetPath( monitor );
    }

    /// <summary>
    /// Checks that the <see cref="TargetPath"/> exists or creates it.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on sucess, false on error.</returns>
    protected bool EnsureTargetPath( IActivityMonitor monitor )
    {
        if( !Path.Exists( _targetPath ) )
        {
            monitor.Info( $"Creating code generated target path: {_targetPath}" );
            try
            {
                Directory.CreateDirectory( _targetPath );
            }
            catch( Exception ex )
            {
                monitor.Error( $"While creating code generated target path: {_targetPath}", ex );
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Checks that if <see cref="ResCoreData.WatchRoot"/> is not null, the <see cref="TargetPath"/>
    /// is independent of <see cref="ResCoreData.LiveStatePath"/> and the "&lt;App&gt;" resources folder
    /// (not above not below them).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="data">The resource space.</param>
    /// <returns>True on success, false on error.</returns>
    protected bool CheckLiveStateCoherency( IActivityMonitor monitor, ResCoreData data )
    {
        if( data.WatchRoot != null )
        {
            var appResourcesLocalPath = data.AppPackage.Resources.LocalPath;
            if( appResourcesLocalPath != null
               && (appResourcesLocalPath.StartsWith( TargetPath ) || TargetPath.StartsWith( appResourcesLocalPath )) )
            {
                monitor.Error( $"""
                    Invalid AppResourcesLocalPath: it must not be above or below TargetPath.
                    CKGenPath: {TargetPath}
                    AppResourcesLocalPath: {appResourcesLocalPath}
                    """ );
                return false;
            }
            var liveStatePath = data.LiveStatePath;
            if( liveStatePath.StartsWith( TargetPath ) || TargetPath.StartsWith( liveStatePath ) )
            {
                monitor.Error( $"""
                Invalid LiveStatePath: it must not be above or below TargetPath.
                CKGenPath: {TargetPath}
                LiveStatePath: {liveStatePath}
                """ );
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// See <see cref="IResourceSpaceItemInstaller.Close(IActivityMonitor, bool)"/>.
    /// Does nothing at this level.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="success">Whether the install succeeded or not.</param>
    public virtual void Close( IActivityMonitor monitor, bool success )
    {
    }

    /// <summary>
    /// Gets the target path. Ends with <see cref="Path.DirectorySeparatorChar"/>.
    /// <para>
    /// <see cref="Open(IActivityMonitor, ResSpace)"/> creates it if needed.
    /// </para>
    /// </summary>
    public string TargetPath => _targetPath;

    /// <summary>
    /// Called before each write with the full path of the file that will be written.
    /// <para>
    /// Does nothing at this level.
    /// </para>
    /// </summary>
    /// <param name="path">The full file path.</param>
    protected virtual void OnWrite( string path )
    {
    }

    /// <inheritdoc />
    public void Write( ResourceLocator resource )
    {
        string path = GetTargetPathAndEnsureDirectory( resource.ResourceName );
        DoWrite( path, resource );
    }

    /// <inheritdoc />
    public void Write( NormalizedPath filePath, ResourceLocator resource )
    {
        string path = GetTargetPathAndEnsureDirectory( filePath.Path );
        DoWrite( path, resource );
    }

    /// <inheritdoc />
    public void Write( NormalizedPath filePath, string text )
    {
        string path = GetTargetPathAndEnsureDirectory( filePath.Path );
        OnWrite( path );
        File.WriteAllText( path, text );
    }

    /// <inheritdoc />
    public Stream OpenWriteStream( NormalizedPath filePath )
    {
        string path = GetTargetPathAndEnsureDirectory( filePath.Path );
        OnWrite( path );
        return new FileStream( path, FileMode.Create );
    }

    void DoWrite( string filePath, ResourceLocator resource )
    {
        OnWrite( filePath );
        if( resource.LocalFilePath != null )
        {
            File.Copy( resource.LocalFilePath, filePath, overwrite: true );
        }
        else
        {
            using( var f = new FileStream( filePath, FileMode.Create ) )
            {
                resource.WriteStream( f );
            }
        }
    }

    /// <inheritdoc />
    public bool SafeDelete( IActivityMonitor monitor, NormalizedPath path )
    {
        var sPath = GetTargetPath( path.Path ).ToString();
        if( File.Exists( sPath ) )
        {
            int retryCount = 0;
            retry:
            try
            {
                File.Delete( sPath );
                monitor.Trace( $"Deleted file '{sPath}'." );
            }
            catch( Exception ex )
            {
                if( ++retryCount < 3 )
                {
                    monitor.Warn( $"While deleting file '{sPath}'. Retrying.", ex );
                    Thread.Sleep( retryCount * 100 );
                    goto retry;
                }
                monitor.Warn( $"Unable to delete file '{sPath}'.", ex );
                return false;
            }
        }
        return true;
    }

    string GetTargetPathAndEnsureDirectory( ReadOnlySpan<char> resName )
    {
        ReadOnlySpan<char> sPath = GetTargetPath( resName );
        // No choice here: we must instantiate a string to ensure that the directory exists.
        // Caching the known directories may not be a great idea or is it?
        // Perf tests here may be welcome...
        var sDir = Path.GetDirectoryName( sPath );
        if( sDir.Length != 0 )
        {
            Directory.CreateDirectory( sDir.ToString() );
        }
        return sPath.ToString();
    }

    ReadOnlySpan<char> GetTargetPath( ReadOnlySpan<char> resName )
    {
        var dest = _pathBuffer.AsSpan( _targetPath.Length, resName.Length );
        resName.CopyTo( dest );
        if( Path.DirectorySeparatorChar != NormalizedPath.DirectorySeparatorChar )
        {
            dest.Replace( NormalizedPath.DirectorySeparatorChar, Path.DirectorySeparatorChar );
        }
        var sPath = _pathBuffer.AsSpan( 0, _targetPath.Length + resName.Length );
        return sPath;
    }

    /// <summary>
    /// Pushes a temporary sub path after the current <see cref="TargetPath"/>.
    /// </summary>
    /// <param name="subPath">Sub path must not be empty nor <see cref="NormalizedPath.IsRooted"/>.</param>
    /// <returns>A disposable that restores the initial target path.</returns>
    public IDisposable PushSubPath( NormalizedPath subPath )
    {
        Throw.CheckArgument( !subPath.IsEmptyPath && !subPath.IsRooted );
        var previous = _targetPath;

        int targetLen = _targetPath.Length;
        subPath.Path.CopyTo( _pathBuffer.AsSpan( targetLen ) );
        targetLen += subPath.Path.Length;
        _pathBuffer[targetLen++] = Path.DirectorySeparatorChar;

        _targetPath = new string( _pathBuffer, 0, targetLen );
        return Util.CreateDisposableAction( () => _targetPath = previous );
    }

    /// <summary>
    /// Overridden to return "Installer: <see cref="TargetPath"/>".
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"Installer: {_targetPath}";
}
