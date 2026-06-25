
using CK.EmbeddedResources;

namespace CK.Core;

sealed class LocalesCache : ResPackageDataCache<FinalTranslationSet>
{
    readonly ActiveCultureSet _activeCultures;
    readonly string _rootFolderName;

    public LocalesCache( ICoreDataCache cache, ActiveCultureSet activeCultures, string rootFolderName )
        : base( cache )
    {
        _activeCultures = activeCultures;
        _rootFolderName = rootFolderName;
    }

    public ActiveCultureSet ActiveCultures => _activeCultures;

    protected override FinalTranslationSet Aggregate( FinalTranslationSet data1, FinalTranslationSet data2 )
    {
        return data1.Aggregate( data2 );
    }

    protected override FinalTranslationSet? Combine( IActivityMonitor monitor, IResPackageResources resources, FinalTranslationSet data )
    {
        if( resources.Resources.LoadTranslations( monitor,
                                                  _activeCultures,
                                                  out var definitions,
                                                  _rootFolderName,
                                                  isOverrideFolder: resources.IsAppResources ) )
        {
            return definitions != null
                    ? definitions.Combine( monitor, data )
                    : data;
        }
        return null;
    }

    protected override FinalTranslationSet? Create( IActivityMonitor monitor, ResPackage package )
    {
        FinalTranslationSet? initial = null;
        if( package.Resources.Resources.LoadTranslations( monitor,
                                                          _activeCultures,
                                                          out var definitions,
                                                          _rootFolderName ) )
        {
            initial = definitions != null
                            ? definitions.ToInitialFinalSet( monitor )
                            : new FinalTranslationSet( _activeCultures );
        }
        return initial;
    }

}
