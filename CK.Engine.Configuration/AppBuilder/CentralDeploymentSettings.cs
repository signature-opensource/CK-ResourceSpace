using CK.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CK.Setup;

/// <summary>
/// The settings of a deployment that are not configuration (see <see cref="CentralDeployment"/>): the <c>"Deployment"</c>
/// root key of the central configuration (the defaults of every deployment) or of an overlay (merged like the rest):
/// <code>
/// "Deployment": {
///   // The AppContexts to deploy (their names): all by default.
///   "AppContexts": [ "Front", "Admin" ],
///   // One asset for the deployment ("Production.zip") instead of one per App ("Production-Auth3.Front.zip"...).
///   "SingleZipAsset": false,
///   // The MSBuild properties of the publication of every host, applied as-is ("dotnet publish" defaults otherwise).
///   "Publish": { "RuntimeIdentifier": "linux-x64", "SelfContained": true, "PublishSingleFile": true },
///   // The ones of an App (by its name), over them: an empty string resets a property (the SDK default).
///   "Apps": { "$Worker": { "Publish": { "PublishSingleFile": "", "InvariantGlobalization": true } },
///             // The packages that the deployment adds to an App: their identifiers (their versions are elsewhere).
///             "$Front": { "Packages": [ "Some.Extension" ] } }
/// }
/// </code>
/// The added packages (<see cref="GetPackages(string)"/>) are opaque here: what a package does is its own business (its
/// types, its aspects, its MSBuild files), the deployment only adds it to the App.
/// The publication properties are not modeled: only their shape is checked (an MSBuild property name, a string, a
/// number or a boolean); MSBuild and the .NET SDK report the rest when the hosts are published. The settings are merged
/// like the rest (a null removes a key of the merged settings: a deployment removes an inherited property with null).
/// </summary>
public sealed partial class CentralDeploymentSettings
{
    readonly IReadOnlyDictionary<string, string> _publish;
    readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _apps;
    readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _packages;

    CentralDeploymentSettings( IReadOnlyList<string>? appContexts,
                               bool singleZipAsset,
                               IReadOnlyDictionary<string, string> publish,
                               IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> apps,
                               IReadOnlyDictionary<string, IReadOnlyList<string>> packages )
    {
        AppContexts = appContexts;
        SingleZipAsset = singleZipAsset;
        _publish = publish;
        _apps = apps;
        _packages = packages;
    }

    /// <summary>
    /// The default settings: all the AppContexts, the defaults of "dotnet publish".
    /// </summary>
    public static readonly CentralDeploymentSettings Default = new( null,
                                                                    false,
                                                                    new Dictionary<string, string>(),
                                                                    new Dictionary<string, IReadOnlyDictionary<string, string>>(),
                                                                    new Dictionary<string, IReadOnlyList<string>>() );

    /// <summary>
    /// Gets the names of the AppContexts to deploy, null for all of them.
    /// </summary>
    public IReadOnlyList<string>? AppContexts { get; }

    /// <summary>
    /// Gets whether the deployment is one asset ("Production.zip": a folder per App in it) instead of one per App
    /// ("Production-Auth3.Front.zip": the host in it), the default.
    /// </summary>
    public bool SingleZipAsset { get; }

    /// <summary>
    /// Gets the MSBuild properties of the publication of every host (the <c>"Publish"</c> of the settings).
    /// </summary>
    public IReadOnlyDictionary<string, string> Publish => _publish;

    /// <summary>
    /// Gets the MSBuild properties of the publication of the host of an App: the <see cref="Publish"/> properties, the
    /// ones of the App (<c>"Apps": { "Name": { "Publish": { ... } } }</c>) over them (an empty value resets a property:
    /// the SDK default).
    /// </summary>
    /// <param name="appName">The App name.</param>
    /// <returns>The properties, sorted by name.</returns>
    public IReadOnlyList<(string Name, string Value)> GetPublishProperties( string appName )
    {
        var result = new Dictionary<string, string>( _publish, StringComparer.OrdinalIgnoreCase );
        var app = _apps.FirstOrDefault( kv => kv.Key.Equals( appName, StringComparison.OrdinalIgnoreCase ) ).Value;
        if( app != null )
        {
            foreach( var (name, value) in app ) result[name] = value;
        }
        return result.OrderBy( kv => kv.Key, StringComparer.OrdinalIgnoreCase ).Select( kv => (kv.Key, kv.Value) ).ToList();
    }

    /// <summary>
    /// Gets the packages that the deployment adds to an App (<c>"Apps": { "Name": { "Packages": [ ... ] } }</c>): their
    /// identifiers, without version.
    /// </summary>
    /// <param name="appName">The App name.</param>
    /// <returns>The package identifiers (empty when the deployment adds none).</returns>
    public IReadOnlyList<string> GetPackages( string appName )
    {
        return _packages.FirstOrDefault( kv => kv.Key.Equals( appName, StringComparison.OrdinalIgnoreCase ) ).Value ?? [];
    }

    [GeneratedRegex( "^[A-Za-z_][A-Za-z0-9_.-]*$" )]
    private static partial Regex PropertyName();

    // A NuGet package identifier, without version.
    [GeneratedRegex( "^[A-Za-z0-9_][A-Za-z0-9_.-]*$" )]
    private static partial Regex PackageId();

    /// <summary>
    /// Reads the settings.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="node">The <c>"Deployment"</c> value (null: the defaults).</param>
    /// <param name="configuration">The configuration whose builders can be deployed.</param>
    /// <param name="source">The source, for the errors.</param>
    /// <returns>The settings, or null on error (errors are logged).</returns>
    public static CentralDeploymentSettings? Read( IActivityMonitor monitor, JsonNode? node, CentralConfiguration configuration, string source )
    {
        if( node == null ) return Default;
        var key = CentralDeployment.SettingsKey;
        if( node is not JsonObject o )
        {
            monitor.Error( $"Invalid \"{key}\" of {source}: an object is expected (\"AppContexts\", \"SingleZipAsset\", \"Publish\", \"Apps\")." );
            return null;
        }
        bool success = true;
        List<string>? appContexts = null;
        bool singleZipAsset = false;
        var publish = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
        var apps = new Dictionary<string, IReadOnlyDictionary<string, string>>( StringComparer.OrdinalIgnoreCase );
        var packages = new Dictionary<string, IReadOnlyList<string>>( StringComparer.OrdinalIgnoreCase );
        foreach( var (k, value) in o )
        {
            if( k.Equals( CentralConfiguration.AppContextsKey, StringComparison.OrdinalIgnoreCase ) )
            {
                if( value is not JsonArray a )
                {
                    monitor.Error( $"Invalid \"{key}:{CentralConfiguration.AppContextsKey}\" of {source}: an array of the names of the AppContexts is expected." );
                    success = false;
                    continue;
                }
                appContexts = new List<string>();
                foreach( var item in a )
                {
                    var name = item is JsonValue v && v.TryGetValue<string>( out var s ) ? s.Trim() : null;
                    var c = name != null ? configuration.FindAppContext( name ) : null;
                    if( c == null )
                    {
                        monitor.Error( $"Invalid AppContext '{item?.ToJsonString()}' in \"{key}:{CentralConfiguration.AppContextsKey}\" of {source}: the AppContexts are '{configuration.AppContexts.Select( c => c.Name ).Concatenate( "', '" )}'." );
                        success = false;
                    }
                    else if( !appContexts.Contains( c.Name ) ) appContexts.Add( c.Name );
                }
            }
            else if( k.Equals( "SingleZipAsset", StringComparison.OrdinalIgnoreCase ) )
            {
                if( value is not JsonValue v || !v.TryGetValue( out singleZipAsset ) )
                {
                    monitor.Error( $"Invalid \"{key}:SingleZipAsset\" of {source}: true or false is expected." );
                    success = false;
                }
            }
            else if( k.Equals( "Publish", StringComparison.OrdinalIgnoreCase ) )
            {
                var p = ReadProperties( monitor, value, $"\"{key}:Publish\" of {source}", ref success );
                if( p != null )
                {
                    foreach( var (name, v) in p ) publish[name] = v;
                }
            }
            else if( k.Equals( "Apps", StringComparison.OrdinalIgnoreCase ) )
            {
                if( value is not JsonObject oApps )
                {
                    monitor.Error( $"Invalid \"{key}:Apps\" of {source}: an object keyed by App names is expected." );
                    success = false;
                    continue;
                }
                foreach( var (appName, appValue) in oApps )
                {
                    if( !configuration.AllApps.Any( a => a.Name.Equals( appName, StringComparison.OrdinalIgnoreCase ) ) )
                    {
                        monitor.Error( $"Unknown App '{appName}' in \"{key}:Apps\" of {source}: the Apps are '{configuration.AllApps.Select( a => a.Name ).Concatenate( "', '" )}'." );
                        success = false;
                        continue;
                    }
                    if( appValue is not JsonObject oApp
                        || oApp.Any( kv => !kv.Key.Equals( "Publish", StringComparison.OrdinalIgnoreCase )
                                           && !kv.Key.Equals( "Packages", StringComparison.OrdinalIgnoreCase ) ) )
                    {
                        monitor.Error( $"Invalid \"{key}:Apps:{appName}\" of {source}: an object with \"Publish\" and \"Packages\" is expected." );
                        success = false;
                        continue;
                    }
                    foreach( var (appKey, v) in oApp )
                    {
                        if( appKey.Equals( "Publish", StringComparison.OrdinalIgnoreCase ) )
                        {
                            var p = ReadProperties( monitor, v, $"\"{key}:Apps:{appName}:Publish\" of {source}", ref success );
                            if( p != null ) apps[appName] = p;
                        }
                        else
                        {
                            var ids = ReadPackages( monitor, v, $"\"{key}:Apps:{appName}:Packages\" of {source}", ref success );
                            if( ids != null ) packages[appName] = ids;
                        }
                    }
                }
            }
            else
            {
                monitor.Error( $"Unknown key \"{key}:{k}\" of {source}: \"AppContexts\", \"SingleZipAsset\", \"Publish\" and \"Apps\" are expected." );
                success = false;
            }
        }
        return success ? new CentralDeploymentSettings( appContexts, singleZipAsset, publish, apps, packages ) : null;

        static List<string>? ReadPackages( IActivityMonitor monitor, JsonNode? node, string source, ref bool success )
        {
            // A null of the central configuration (a merge patch removes the ones of the overlays): no package.
            if( node == null ) return null;
            if( node is not JsonArray a )
            {
                monitor.Error( $"Invalid {source}: an array of package identifiers is expected ([ \"Some.Package\" ])." );
                success = false;
                return null;
            }
            var result = new List<string>();
            foreach( var item in a )
            {
                var id = item is JsonValue v && v.TryGetValue<string>( out var s ) ? s.Trim() : null;
                if( id == null || !PackageId().IsMatch( id ) )
                {
                    monitor.Error( $"Invalid package '{item?.ToJsonString()}' in {source}: a package identifier is expected, without version (the version of an added package is the one of the World)." );
                    success = false;
                }
                else if( !result.Contains( id, StringComparer.OrdinalIgnoreCase ) ) result.Add( id );
            }
            return result;
        }

        static Dictionary<string, string>? ReadProperties( IActivityMonitor monitor, JsonNode? node, string source, ref bool success )
        {
            if( node == null ) return null;
            if( node is not JsonObject o )
            {
                monitor.Error( $"Invalid {source}: an object of MSBuild properties is expected ({{ \"RuntimeIdentifier\": \"linux-x64\" }})." );
                success = false;
                return null;
            }
            var result = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
            foreach( var (name, value) in o )
            {
                if( !PropertyName().IsMatch( name ) )
                {
                    monitor.Error( $"Invalid MSBuild property name '{name}' in {source}." );
                    success = false;
                    continue;
                }
                // A null of the central configuration (a merge patch removes the ones of the overlays): no property.
                if( value == null ) continue;
                if( value is JsonValue v && v.GetValueKind() is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False )
                {
                    result[name] = v.GetValueKind() switch
                    {
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.String => v.GetValue<string>(),
                        _ => v.ToJsonString()
                    };
                }
                else
                {
                    monitor.Error( $"Invalid value of '{name}' in {source}: a string, a number or a boolean is expected." );
                    success = false;
                }
            }
            return result;
        }
    }
}
