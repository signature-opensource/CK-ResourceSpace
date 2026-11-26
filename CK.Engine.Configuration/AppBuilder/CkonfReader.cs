using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CK.Setup;

/// <summary>
/// What the "ckonf.json" files share: the reading (comments allowed, case insensitive keys), the names, and the App or
/// Apps of an AppContext ("App": { ... } XOR "Apps": [ { "Name": "$Party" }, ... ]).
/// </summary>
static class CkonfReader
{
    static readonly JsonDocumentOptions _documentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// Reads a "ckonf.json" file: its JSON (case insensitive) and its configuration section ("ckonf").
    /// A duplicate key (case insensitive) is an error.
    /// </summary>
    public static bool TryRead( IActivityMonitor monitor,
                                NormalizedPath file,
                                out JsonObject root,
                                out ImmutableConfigurationSection section )
    {
        root = null!;
        section = null!;
        try
        {
            return TryParse( monitor, file, File.ReadAllText( file ), out root, out section );
        }
        catch( Exception ex )
        {
            monitor.Error( $"Invalid '{file}'.", ex );
            return false;
        }
    }

    /// <summary>
    /// Parses the content of a "ckonf.json" file (see <see cref="TryRead"/>).
    /// </summary>
    public static bool TryParse( IActivityMonitor monitor,
                                 NormalizedPath file,
                                 string json,
                                 out JsonObject root,
                                 out ImmutableConfigurationSection section )
    {
        root = null!;
        section = null!;
        try
        {
            // The configuration is case insensitive: "Back1" and "back1" are the same key.
            using( var doc = JsonDocument.Parse( json, _documentOptions ) )
            {
                if( !CheckDuplicateKeys( monitor, file, doc.RootElement, "" ) ) return false;
            }
            section = ImmutableConfigurationSection.CreateFromJson( "ckonf", json );
            root = JsonNode.Parse( json, new JsonNodeOptions { PropertyNameCaseInsensitive = true }, _documentOptions ) as JsonObject
                   ?? throw new InvalidDataException( "A JSON object is expected." );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"Invalid '{file}'.", ex );
            return false;
        }
    }

    /// <summary>
    /// Gets the name of the AppContext of a Builder: the name of its folder without ".Builder".
    /// </summary>
    public static string AppContextNameOf( NormalizedPath builderFolder )
    {
        var n = builderFolder.LastPart;
        return n.EndsWith( ".Builder", StringComparison.OrdinalIgnoreCase ) ? n[..^8] : n;
    }

    /// <summary>
    /// Gets the default name of the single App of an AppContext: its name with a '$' before its last segment, the party
    /// (<c>"Worker"</c> -> <c>"$Worker"</c>, <c>"CK.Ng.Admin.Sample"</c> -> <c>"CK.Ng.Admin.$Sample"</c>: its projects
    /// are "CK.Ng.Admin.Sample.App" and ".Host").
    /// </summary>
    public static string DefaultAppName( string appContextName )
    {
        var i = appContextName.LastIndexOf( '.' );
        return i < 0 ? '$' + appContextName : $"{appContextName[..(i + 1)]}${appContextName[(i + 1)..]}";
    }

    /// <summary>
    /// Gets whether a name is a valid AppContext name: letters, digits, '_', '-' and '.', no '$' (it is not a party).
    /// </summary>
    public static bool IsValidAppContextName( string name )
        => name.Length > 0 && name[0] != '.' && name[^1] != '.' && !name.Contains( ".." ) && name.All( c => IsNameChar( c ) || c == '.' );

    /// <summary>
    /// Gets whether a name is a valid App name: its party name (<c>"$Jobs"</c>), possibly after the segments of its domain
    /// relative to the domain of the solution (<c>"Customers.$Acme"</c>). The segments are letters, digits, '_' and '-'.
    /// </summary>
    public static bool IsValidAppName( string name )
    {
        var parts = name.Split( '.' );
        var party = parts[^1];
        return party.Length > 1
               && party[0] == '$'
               && party.Skip( 1 ).All( IsNameChar )
               && parts[..^1].All( p => p.Length > 0 && p.All( IsNameChar ) );
    }

    /// <summary>
    /// Gets the name of the projects of an App without the domain of the solution: its name without the '$'
    /// (<c>"Customers.$Acme"</c> -> <c>"Customers.Acme"</c>).
    /// </summary>
    public static string BareProjectName( string appName ) => appName.Replace( "$", "" );

    static bool IsNameChar( char c ) => char.IsAsciiLetterOrDigit( c ) || c == '_' || c == '-';

    /// <summary>
    /// Reads the App ("App": { ... }, its "Name" defaults to <paramref name="defaultName"/>) or the Apps ("Apps": [ { "Name" ... } ])
    /// of a builder. One of them is required, not both. The names are unique (case insensitive).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="file">The file (for the errors).</param>
    /// <param name="defaultName">The name of the App when "App" has no "Name".</param>
    /// <param name="builder">The JSON of the builder (the root of its "ckonf.json", or its entry in the central file).</param>
    /// <param name="section">The configuration section of the builder.</param>
    /// <returns>The Apps in the order of the file, or null on error.</returns>
    public static List<(string Name, ImmutableConfigurationSection Section, JsonObject Node)>? ReadApps( IActivityMonitor monitor,
                                                                                                    NormalizedPath file,
                                                                                                    string defaultName,
                                                                                                    JsonObject builder,
                                                                                                    ImmutableConfigurationSection section )
    {
        var app = builder["App"];
        var apps = builder["Apps"];
        if( app != null && apps != null )
        {
            monitor.Error( $"\"{section.Path}\" in '{file}' defines \"App\" and \"Apps\": a builder has one App (\"App\": {{ ... }}) or several (\"Apps\": [ {{ \"Name\": ... }}, ... ]), not both." );
            return null;
        }
        var result = new List<(string Name, ImmutableConfigurationSection Section, JsonObject Node)>();
        if( app != null )
        {
            if( app is not JsonObject oApp )
            {
                monitor.Error( $"\"{section.Path}:App\" in '{file}' must be an object." );
                return null;
            }
            var s = GetSection( section, "App" );
            var name = ReadName( monitor, file, s, oApp, defaultName );
            if( name == null ) return null;
            result.Add( (name, s, oApp) );
            return result;
        }
        if( apps is not JsonArray aApps || aApps.Count == 0 )
        {
            monitor.Error( apps == null
                            ? $"\"{section.Path}\" in '{file}' must define its App (\"App\": {{ ... }}) or its Apps (\"Apps\": [ {{ \"Name\": ... }}, ... ])."
                            : $"\"{section.Path}:Apps\" in '{file}' must be an array of Apps with their \"Name\" (at least one)." );
            return null;
        }
        bool success = true;
        var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        for( int i = 0; i < aApps.Count; ++i )
        {
            if( aApps[i] is not JsonObject oApp )
            {
                monitor.Error( $"\"{section.Path}:Apps:{i}\" in '{file}' must be an object." );
                success = false;
                continue;
            }
            var s = GetSection( section, $"Apps:{i}" );
            var name = ReadName( monitor, file, s, oApp, null );
            if( name == null )
            {
                success = false;
                continue;
            }
            if( !names.Add( name ) )
            {
                monitor.Error( $"Duplicate App name '{name}' in \"{section.Path}:Apps\" of '{file}' (the names are case insensitive)." );
                success = false;
                continue;
            }
            result.Add( (name, s, oApp) );
        }
        return success ? result : null;

        static string? ReadName( IActivityMonitor monitor, NormalizedPath file, ImmutableConfigurationSection s, JsonObject o, string? defaultName )
        {
            var n = o["Name"];
            if( n == null )
            {
                if( defaultName == null ) monitor.Error( $"\"{s.Path}\" in '{file}' must have a \"Name\"." );
                return defaultName;
            }
            if( n.GetValueKind() != JsonValueKind.String || !IsValidAppName( n.GetValue<string>().Trim() ) )
            {
                monitor.Error( $"Invalid \"{s.Path}:Name\" in '{file}': the name of an App is its party name (\"$Jobs\"), possibly after the segments of its domain relative to the domain of the solution (\"Customers.$Acme\")." );
                return null;
            }
            return n.GetValue<string>().Trim();
        }
    }

    // An empty object may have no section: an empty one is created.
    static ImmutableConfigurationSection GetSection( ImmutableConfigurationSection parent, string path )
        => parent.TryGetSection( path ) ?? new ImmutableConfigurationSection( new MutableConfigurationSection( $"{parent.Path}:{path}" ) );

    static bool CheckDuplicateKeys( IActivityMonitor monitor, NormalizedPath file, JsonElement e, string path )
    {
        bool success = true;
        if( e.ValueKind == JsonValueKind.Object )
        {
            var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
            foreach( var p in e.EnumerateObject() )
            {
                var pPath = path.Length == 0 ? p.Name : $"{path}:{p.Name}";
                if( !names.Add( p.Name ) )
                {
                    monitor.Error( $"Duplicate key '{pPath}' in '{file}' (the keys are case insensitive)." );
                    success = false;
                }
                success &= CheckDuplicateKeys( monitor, file, p.Value, pPath );
            }
        }
        else if( e.ValueKind == JsonValueKind.Array )
        {
            int i = 0;
            foreach( var item in e.EnumerateArray() ) success &= CheckDuplicateKeys( monitor, file, item, $"{path}:{i++}" );
        }
        return success;
    }
}
