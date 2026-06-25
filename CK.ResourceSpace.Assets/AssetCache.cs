
using CK.EmbeddedResources;

namespace CK.Core;

sealed class AssetCache : ResPackageDataCache<FinalResourceAssetSet>
{
    readonly string _rootFolderName;

    public AssetCache( ICoreDataCache cache, string rootFolderName )
        : base( cache )
    {
        _rootFolderName = rootFolderName;
    }

    protected override FinalResourceAssetSet Aggregate( FinalResourceAssetSet data1, FinalResourceAssetSet data2 )
    {
        return data1.Aggregate( data2 );
    }

    protected override FinalResourceAssetSet? Combine( IActivityMonitor monitor, IResPackageResources resources, FinalResourceAssetSet data )
    {
        if( resources.Resources.LoadAssets( monitor,
                                            resources.Package.DefaultTargetPath,
                                            out var definitions,
                                            _rootFolderName ) )
        {
            return definitions != null
                    ? definitions.Combine( monitor, data )
                    : data;
        }
        return null;
    }

    protected override FinalResourceAssetSet? Create( IActivityMonitor monitor, ResPackage package )
    {
        FinalResourceAssetSet? initial = null;
        if( package.Resources.Resources.LoadAssets( monitor,
                                                    package.DefaultTargetPath,
                                                    out var definitions,
                                                    _rootFolderName ) )
        {
            initial = definitions != null
                            ? definitions.ToInitialFinalSet( monitor )
                            : FinalResourceAssetSet.Empty;
        }
        return initial;
    }
}
