using CK.EmbeddedResources;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CK.Core;

/// <summary>
/// Locales folder handler.
/// See <see cref="ResourceContainerGlobalizationExtension.LoadTranslations(IResourceContainer, CK.Core.IActivityMonitor, CK.Core.ActiveCultureSet, out TranslationDefinitionSet?, string, bool)"/>.
/// <para>
/// This can be specialized: a static <see cref="ReadLiveState"/> must always be implemented.
/// </para>
/// </summary>
public partial class LocalesResourceHandler : ResourceSpaceFolderHandler
{
    readonly LocalesCache _cache;
    readonly InstallOption _installOption;
    FinalTranslationSet? _finalTranslations;

    /// <summary>
    /// Specify how the final locale translations files must be generated.
    /// </summary>
    [Flags]
    public enum InstallOption
    {
        /// <summary>
        /// Generates a file for each culture in the <see cref="ActiveCultureSet"/> with the
        /// <see cref="IFinalTranslationSet.RootPropagatedTranslations"/> even when there is no translations.
        /// </summary>
        Full = 0,

        /// <summary>
        /// Generates a file for each culture in the <see cref="ActiveCultureSet"/> with its
        /// <see cref="IFinalTranslationSet.Translations"/> even when there is no translations.
        /// </summary>
        Minimal = 1,

        /// <summary>
        /// When set, this bit prevents an empty set to be generated.
        /// </summary>
        WithoutEmptySet = 2,

        /// <summary>
        /// When set, this bit sorts the keys in the final translation sets.
        /// </summary>
        WithSortedKeys = 4
    }

    /// <summary>
    /// Initializes a new locales resources handler.
    /// </summary>
    /// <param name="installer">The installer to use.</param>
    /// <param name="packageDataCache">The package data cache.</param>
    /// <param name="rootFolderName">The folder name (typically "locales", "ts-locales", etc.).</param>
    /// <param name="activeCultures">The required active cultures.</param>
    /// <param name="installOption">How the final locale files must be generated.</param>
    public LocalesResourceHandler( IResourceSpaceItemInstaller? installer,
                                   ICoreDataCache packageDataCache,
                                   string rootFolderName,
                                   ActiveCultureSet activeCultures,
                                   InstallOption installOption )
        : base( installer, rootFolderName )
    {
        _cache = new LocalesCache( packageDataCache, activeCultures, RootFolderName );
        _installOption = installOption;
    }

    /// <summary>
    /// Gets the cache instance to which this data handler is bound.
    /// </summary>
    public ICoreDataCache ResPackageDataCache => _cache.SpaceCache;

    /// <summary>
    /// Gets the final assets that have been successfully initialized.
    /// <see cref="FinalTranslationSet.IsAmbiguous"/> is necessarily false.
    /// </summary>
    public FinalTranslationSet? FinalTranslations => _finalTranslations;

    /// <summary>
    /// Gets whether this is empty: there is only the "en" <see cref="ActiveCultureSet.Root"/> default active culture
    /// and there is no translation at all for this root.
    /// <para>
    /// This is always empty until <see cref="Initialize(IActivityMonitor, ResCoreData)"/> is called.
    /// </para>
    /// </summary>
    public bool IsEmpty => _cache.ActiveCultures.Count == 1 && _finalTranslations?.Translations.Count == 0;

    /// <summary>
    /// Checks that the <see cref="FinalTranslations"/> is not ambiguous.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="spaceData">The resource data.</param>
    /// <returns>True if the FinalTranslations can be successfully computed and that no ambiguous resources exist.</returns>
    protected override bool Initialize( IActivityMonitor monitor, ResCoreData spaceData )
    {
        _finalTranslations = GetUnambiguousFinalTranslations( monitor, spaceData );
        return _finalTranslations != null;
    }

    FinalTranslationSet? GetUnambiguousFinalTranslations( IActivityMonitor monitor, ResCoreData spaceData )
    {
        FinalTranslationSet? r = _cache.Obtain( monitor, spaceData.AppPackage );
        if( r != null )
        {
            if( r.IsAmbiguous )
            {
                var ambiguities = r.Ambiguities
                                          .Select( kv => $"""
                                      '{kv.Key}' is defined in {kv.Value.Origin} with text '{kv.Value.Text}'.
                                      But is also defined in:
                                      {kv.Value.Ambiguities!.Select( r => $"{r.Origin.ToString()} with text '{r.Text}'." ).Concatenate( Environment.NewLine )}.
                                      """ );

                monitor.Error( $"""
                Ambiguities detected in final translations:
                {ambiguities.Concatenate( Environment.NewLine )}
                """ );
                return null;
            }
        }
        return r;
    }

    /// <summary>
    /// Gets the active cultures.
    /// </summary>
    protected ActiveCultureSet ActiveCultures => _cache.ActiveCultures;

    /// <summary>
    /// Saves the initialized <see cref="FinalTranslations"/> into into this <see cref="ResourceSpaceFolderHandler.Installer"/>.
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
        // We don't skip here if IsEmpty is true: we want the translations files
        // to be available (empty) so the structure is here.
        Throw.CheckState( _finalTranslations != null );
        using( Installer.PushSubPath( RootFolderName ) )
        {
            return WriteFinal( monitor, _finalTranslations, Installer );
        }
    }

    bool WriteFinal( IActivityMonitor monitor,
                     FinalTranslationSet final,
                     IResourceSpaceItemInstaller installer )
    {
        try
        {
            if( (_installOption & InstallOption.WithoutEmptySet) != 0 )
            {
                WriteExistingSets( final,
                                   installer,
                                   rootPropagated: (_installOption & InstallOption.Minimal) == 0,
                                   sortKeys: (_installOption & InstallOption.WithSortedKeys) != 0 );
            }
            else
            {
                WriteAllCultures( final,
                                  installer,
                                   rootPropagated: (_installOption & InstallOption.Minimal) == 0,
                                   sortKeys: (_installOption & InstallOption.WithSortedKeys) != 0 );
            }
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( "While saving FinalTranslations.", ex );
            return false;
        }


        static void WriteExistingSets( FinalTranslationSet final, IResourceSpaceItemInstaller target, bool rootPropagated, bool sortKeys )
        {
            foreach( var set in final.AllTranslationSets.Where( set => set.Translations.Count > 0 ) )
            {
                var translations = rootPropagated
                                    ? set.RootPropagatedTranslations
                                    : set.Translations;
                WriteJson( target, $"{set.Culture.Culture.Name}.json", translations, sortKeys );
            }
        }

        static void WriteAllCultures( FinalTranslationSet final, IResourceSpaceItemInstaller target, bool rootPropagated, bool sortKeys )
        {
            foreach( var c in final.Culture.ActiveCultures.AllActiveCultures )
            {
                var set = final.FindTranslationSetOrParent( c );
                var translations = rootPropagated
                                        ? set.RootPropagatedTranslations
                                        : set.Translations;
                var fPath = $"{c.Culture.Name}.json";
                WriteJson( target, fPath, translations, sortKeys );
            }
        }

        static void WriteJson( IResourceSpaceItemInstaller target,
                               NormalizedPath fPath,
                               IEnumerable<KeyValuePair<string, FinalTranslationValue>> translations,
                               bool sortKeys )
        {
            using( var s = target.OpenWriteStream( fPath ) )
            using( var w = new Utf8JsonWriter( s, new JsonWriterOptions() { Indented = true } ) )
            {
                w.WriteStartObject();
                foreach( var t in sortKeys ? translations.OrderBy( kv => kv.Key ) : translations )
                {
                    w.WriteString( t.Key, t.Value.Text );
                }
                w.WriteEndObject();
            }
        }
    }

}
