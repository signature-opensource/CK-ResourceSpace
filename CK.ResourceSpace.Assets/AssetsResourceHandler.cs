
using CK.EmbeddedResources;
using System;
using System.Linq;

namespace CK.Core;

/// <summary>
/// Assets folder handler.
/// See <see cref="ResourceContainerAssetsExtension.LoadAssets(IResourceContainer, IActivityMonitor, NormalizedPath, out ResourceAssetDefinitionSet?, string)"/>.
/// <para>
/// Live support currently uses no cache.
/// </para>
/// </summary>
public partial class AssetsResourceHandler : ResourceSpaceFolderHandler
{
    readonly AssetCache _cache;
    FinalResourceAssetSet? _finalAssets;

    /// <summary>
    /// Initializes a new assets resources handler.
    /// </summary>
    /// <param name="installer">The installer to use.</param>
    /// <param name="packageDataCache">The package data cache.</param>
    /// <param name="rootFolderName">The folder name (typically "assets", "ts-assets", etc.).</param>
    public AssetsResourceHandler( IResourceSpaceItemInstaller? installer,
                                  ICoreDataCache packageDataCache,
                                  string rootFolderName )
        : base( installer, rootFolderName )
    {
        _cache = new AssetCache( packageDataCache, rootFolderName );
    }

    /// <summary>
    /// Gets the cache instance to which this data handler is bound.
    /// </summary>
    public ICoreDataCache ResPackageDataCache => _cache.SpaceCache;

    /// <summary>
    /// Gets the final assets that have been successfully initialized.
    /// <see cref="FinalResourceAssetSet.IsAmbiguous"/> is necessarily false.
    /// </summary>
    public FinalResourceAssetSet? FinalAssets => _finalAssets;

    /// <inheritdoc />
    protected override bool Initialize( IActivityMonitor monitor, ResCoreData spaceData )
    {
        _finalAssets = GetUnambiguousFinalAssets( monitor, spaceData );
        return _finalAssets != null;
    }

    FinalResourceAssetSet? GetUnambiguousFinalAssets( IActivityMonitor monitor, ResCoreData spaceData )
    {
        FinalResourceAssetSet? r = _cache.Obtain( monitor, spaceData.AppPackage );
        if( r != null )
        {
            if( r.IsAmbiguous )
            {
                var ambiguities = r.Assets.Where( kv => kv.Value.Ambiguities != null )
                                          .Select( kv => $"'{kv.Key}' is mapped by {kv.Value.Origin} but also to {kv.Value.Ambiguities!.Select( r => r.ToString() ).Concatenate()}." );
                monitor.Error( $"""
                Ambiguities detected in assets:
                {ambiguities.Concatenate( Environment.NewLine )}
                """ );
                return null;
            }
        }
        return r;
    }

    /// <summary>
    /// Saves the initialized <see cref="FinalAssets"/> into this <see cref="ResourceSpaceFolderHandler.Installer"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on succes, false one error (errors have been logged).</returns>
    protected override bool Install( IActivityMonitor monitor )
    {
        if( Installer is null )
        {
            monitor.Warn( $"No installer associated to '{ToString()}'. Skipped." );
            return true;
        }
        Throw.CheckState( FinalAssets != null );
        using( Installer.PushSubPath( RootFolderName ) )
        {
            return WriteFinal( monitor, FinalAssets, Installer );
        }
    }

    static bool WriteFinal( IActivityMonitor monitor,
                            FinalResourceAssetSet f,
                            IResourceSpaceItemInstaller installer )
    {
        try
        {
            foreach( var a in f.Assets )
            {
                installer.Write( a.Key, a.Value.Origin );
            }
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( "While generating Assets.", ex );
            return false;
        }
    }
}
