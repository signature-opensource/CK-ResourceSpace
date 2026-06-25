using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CK.EmbeddedResources;

/// <summary>
/// Extends <see cref="IResourceContainer"/> with LoadLocales methods.
/// </summary>
public static class ResourceContainerGlobalizationExtension
{
    /// <summary>
    /// Processes the <paramref name="folder"/> if it exists and returns a <see cref="TranslationDefinitionSet"/>.
    /// Returns false on error (error has been logged).
    /// <para>
    /// The following folder:
    /// <code>
    /// kind-locales/
    ///   default.jsonc
    ///   fr-FR.json
    ///   en/
    ///     en-GB.jsonc
    ///   en-US.jsonc
    ///   de.jsonc
    /// </code>
    /// Will produce the "en" (from the <c>default.jsonc</c> file), the "en-GB", "en-US", "fr-FR" and "de" culture set.
    /// Folders are ignored: they can freely be used to group related files.
    /// </para>
    /// <para>
    /// Files must be ".jsonc" (json with comments), ".json" files are prohibited.
    /// Parent cultures are not created if not needed: here the "fr" set is not created.
    /// </para>
    /// <para>
    /// The <c>default.jsonc</c> file is required unless <paramref name="isOverrideFolder"/> is true.
    /// </para>
    /// <para>
    /// When <paramref name="isOverrideFolder"/> is true, the loaded folder is an override that doesn't define new resources.
    /// It doesn't need a <c>default.jsonc</c> and all resources in its files are considered to have an implicit "O:" key prefix.
    /// </para>
    /// </summary>
    /// <param name="container">This container of resources.</param>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="activeCultures">The cultures to consider. Cultures not in this set are skipped.</param>
    /// <param name="translations">The loaded translations. Can be null on success if no "<paramref name="folder"/>/" exists.</param>
    /// <param name="folder">The folder to load (typically "locales" or "ts-locales").</param>
    /// <param name="isOverrideFolder">True for pure override folder (no new resources are allowed).</param>
    /// <returns>True on success, false on error.</returns>
    public static bool LoadTranslations( this IResourceContainer container,
                                         IActivityMonitor monitor,
                                         ActiveCultureSet activeCultures,
                                         out TranslationDefinitionSet? translations,
                                         string folder,
                                         bool isOverrideFolder = false )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( folder );
        if( container.TryGetFolder( folder, out var content ) )
        {
            bool unactiveCultureWarned = false;
            using( monitor.OpenInfo( $"Reading {content}." ) )
            {
                var defaultSet = CreateRoot( monitor, activeCultures, content, isOverrideFolder );
                if( defaultSet != null )
                {
                    monitor.Trace( $"Successfully loaded {defaultSet.Translations.Count} translations from {defaultSet.Origin}." );
                    translations = ReadTranslations( monitor, defaultSet, isOverrideFolder, content.AllResources, activeCultures, ref unactiveCultureWarned );
                    return translations != null;
                }
            }
        }
        translations = null;
        return true;
    }

    static TranslationDefinitionSet? CreateRoot( IActivityMonitor monitor,
                                                 ActiveCultureSet activeCultures,
                                                 ResourceFolder folder,
                                                 bool isOverrideFolder )
    {
        if( folder.TryGetResource( "default.jsonc", out var defFile )
            || isOverrideFolder && folder.TryGetResource( "en.jsonc", out defFile ) )
        {
            if( ReadJson( monitor, defFile, isOverrideFolder, out var defTranslations ) )
            {
                return new TranslationDefinitionSet( activeCultures, defFile, defTranslations );
            }
        }
        else
        {
            // We didn't find a default (or an "en" if isOverrideFolder is true).
            if( isOverrideFolder )
            {
                // If isOverrideFolder, simply returns an empty "en" root set.
                return new TranslationDefinitionSet( activeCultures, defFile, null );
            }
            // Regular folder MUST contain a "default".
            monitor.Error( $"Missing 'default.jsonc' file in {folder}. This file must contain all the resources in english." );
        }
        return null;
    }

    static TranslationDefinitionSet? ReadTranslations( IActivityMonitor monitor,
                                                       TranslationDefinitionSet root,
                                                       bool isOverrideFolder,
                                                       IEnumerable<ResourceLocator> allResources,
                                                       ActiveCultureSet activeCultures,
                                                       ref bool unactiveCultureWarned )
    {
        bool success = true;

        var others = allResources.Where( o => o != root.Origin );
        foreach( var o in others )
        {
            if( !o.ResourceName.EndsWith( ".jsonc" ) )
            {
                monitor.Error( $"Invalid '{o}'. Only '.jsonc' files must appear in locales folder." );
                success = false;
                continue;
            }
            var cName = Path.GetFileNameWithoutExtension( o.FullResourceName );
            if( !NormalizedCultureInfo.IsValidCultureName( cName ) )
            {
                monitor.Error( $"Invalid '{o}'. Name '{cName}' is not a BCP47 compliant culture name." );
                success = false;
            }
            else
            {
                var c = ExtendedCultureInfo.All.FindNormalizedCultureInfo( cName );
                if( c == null || !activeCultures.TryGet( c, out ActiveCulture? aC ) )
                {
                    monitor.Warn( $"Ignoring translation file for '{cName}' as it doesn't appear in the active cultures." );
                    if( !unactiveCultureWarned )
                    {
                        unactiveCultureWarned = true;
                        monitor.Warn( $"Active cultures are: {activeCultures.AllActiveCultures.Select( c => c.Culture.Name ).Concatenate()}." );
                    }
                }
                else if( c == NormalizedCultureInfo.CodeDefault )
                {
                    //
                    // When isOverrideFolder is true, finding an "en" here means there were a "default.jsonc" (because of the r != defaultSet.Origin
                    // condition in the others): the error can be the same as with a false isOverrideFolder.
                    //
                    monitor.Error( $"File {o} defines the \"en\" culture. This is the default culture that must be in 'default.jsonc' file." );
                    success = false;
                }
                else if( root.CheckNoSubSet( monitor, aC, cName, o ) )
                {
                    success &= ReadSpecificSet( monitor, o, aC, root, isOverrideFolder );
                }
            }
        }
        return success ? root : null;

        static bool ReadSpecificSet( IActivityMonitor monitor,
                                     ResourceLocator locator,
                                     ActiveCulture culture,
                                     TranslationDefinitionSet root,
                                     bool isOverrideFolder )
        {
            if( ReadJson( monitor, locator, isOverrideFolder, out var content ) )
            {
                bool success = true;
                foreach( var kv in content )
                {
                    // When a key overrides an entry, it overrides a key defined in another component.
                    // The fact that "fr-FR" overrides the "Super.EvenBetter.Text" key does NOT mean that
                    // this key must also be defined as an override in the "default.jsonc" file: it has to be defined
                    // in the final merged set by a lower-level component. Override handling is done by the FinalTranslationSet,
                    // not here.
                    if( kv.Value.Override == ResourceOverrideKind.None && !root.Translations.ContainsKey( kv.Key ) )
                    {
                        monitor.Error( $"""
                                        Missing key in default translation file.
                                        Key '{kv.Key}' defined in translation file {locator} doesn't exist in {root.Origin}.
                                        """ );
                        success = false;
                    }
                }
                if( success )
                {
                    var subSet = root.CreateSubSet( culture, locator, content );
                    monitor.Trace( $"Successfully loaded {subSet.Translations.Count} translations from {locator}." );
                    return true;
                }
            }
            return false;
        }
    }

    static bool ReadJson( IActivityMonitor monitor,
                          ResourceLocator locator,
                          bool isOverrideFolder,
                          [NotNullWhen( true )] out Dictionary<string, TranslationDefinition>? content )
    {
        content = null;
        try
        {
            using var s = locator.GetStream();
            content = ReadJsonTranslationFile( locator, s, isOverrideFolder, skipComments: locator.FullResourceName.EndsWith( ".jsonc" ) );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"Unable to read translations from {locator}.", ex );
            return false;
        }

        static Dictionary<string, TranslationDefinition> ReadJsonTranslationFile( ResourceLocator origin, Stream s, bool isOverrideFolder, bool skipComments )
        {
            var options = new JsonReaderOptions
            {
                CommentHandling = skipComments ? JsonCommentHandling.Skip : JsonCommentHandling.Disallow,
                AllowTrailingCommas = true
            };
            using var context = Utf8JsonStreamReaderContext.Create( s, options, out var reader );
            var result = new Dictionary<string, TranslationDefinition>();
            ReadJson( ref reader, context, origin, isOverrideFolder, result );
            return result;

            static void ReadJson( ref Utf8JsonReader r,
                                  IUtf8JsonReaderContext context,
                                  ResourceLocator origin,
                                  bool isOverrideFolder,
                                  Dictionary<string, TranslationDefinition> target )
            {
                if( r.TokenType == JsonTokenType.None && !r.Read() )
                {
                    Throw.InvalidDataException( $"Expected a json object." );
                }
                Throw.CheckData( r.TokenType == JsonTokenType.StartObject );
                ReadObject( ref r, context, origin, isOverrideFolder, target, "" );

                static void ReadObject( ref Utf8JsonReader r,
                                        IUtf8JsonReaderContext context,
                                        ResourceLocator origin,
                                        bool isOverrideFolder,
                                        Dictionary<string, TranslationDefinition> target,
                                        string parentPath )
                {
                    Throw.DebugAssert( r.TokenType == JsonTokenType.StartObject );
                    Throw.DebugAssert( parentPath.Length == 0 || parentPath[^1] == '.' );

                    r.ReadWithMoreData( context );
                    while( r.TokenType == JsonTokenType.PropertyName )
                    {
                        var fullPropertyName = r.GetString();
                        Throw.CheckData( "Expected non empty property name.", !string.IsNullOrWhiteSpace( fullPropertyName ) );
                        Throw.CheckData( "Property name cannot end or start with '.' nor contain '..'.",
                                         fullPropertyName[0] != '.' && fullPropertyName[^1] != '.' && !fullPropertyName.Contains( ".." ) );
                        ResourceOverrideKind overrideKind = isOverrideFolder
                                                                ? ResourceOverrideKind.Regular
                                                                : ResourceOverrideKind.None;
                        var propertyName = fullPropertyName;
                        if( propertyName.StartsWith( "O:" ) )
                        {
                            overrideKind = ResourceOverrideKind.Regular;
                            propertyName = propertyName.Substring( 2 );
                        }
                        else if( propertyName.StartsWith( "?O:" ) )
                        {
                            overrideKind = ResourceOverrideKind.Optional;
                            propertyName = propertyName.Substring( 3 );
                        }
                        else if( propertyName.StartsWith( "!O:" ) )
                        {
                            overrideKind = ResourceOverrideKind.Always;
                            propertyName = propertyName.Substring( 3 );
                        }
                        propertyName = parentPath + propertyName;
                        r.ReadWithMoreData( context );
                        if( r.TokenType == JsonTokenType.StartObject )
                        {
                            if( overrideKind is not ResourceOverrideKind.None )
                            {
                                Throw.InvalidDataException( $"Invalid parent property name \"{fullPropertyName}\": When defining a subordinated object, the parent key must not be an override." );
                            }
                            ReadObject( ref r, context, origin, isOverrideFolder, target, propertyName + '.' );
                        }
                        else
                        {
                            if( r.TokenType != JsonTokenType.String )
                            {
                                Throw.InvalidDataException( $"Expected a string or an object, got a '{r.TokenType}'." );
                            }
                            if( !target.TryAdd( propertyName, new TranslationDefinition( r.GetString()!, overrideKind ) ) )
                            {
                                Throw.InvalidDataException( $"Duplicate key '{propertyName}' found." );
                            }
                        }
                        r.ReadWithMoreData( context );
                    }
                }
            }
        }

    }


}
