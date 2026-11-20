using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CK.Setup;

/// <summary>
/// A deployment of a solution: a folder of its "Deployment/" folder (next to its central "ckonf.json") whose name starts
/// with '#' (the environment) and whose path above it is the domain: "Deployment/#Production" (the central domain),
/// "Deployment/Paris/Shop/#Staging" (the domain "Paris/Shop"). Its <see cref="Name"/> is this path ("#Production",
/// "Paris/Shop/#Staging"). A folder without a '#' folder below it is a regular folder, ignored, and so are
/// "Deployment/Assets" and the folders whose name starts with '.'.
/// <para>
/// Its "ckonf.json" is an overlay (JSON Merge Patch, RFC 7396: an object merges, <c>null</c> removes, any other value,
/// an array included, replaces) over the central configuration, or over the deployment that its <c>"$BASE"</c> names.
/// Its "Domain" and "Environment" come from its path: writing them is an error. The result, <see cref="Merged"/>, is
/// written next to it (<see cref="MergedFileName"/>) by every builder run.
/// </para>
/// <para>
/// A value "per deployment" (an address, a public URL...) must be written by the deployment itself: inherited (from the
/// central configuration or a base deployment), it is an error. They are declared by the configuration
/// (<c>"$PER_DEPLOYMENT": [ "Demo", "Links:[*]:Link:KeyManagement:AutoTrustKey" ]</c> on any object, the patterns relative
/// to it) and by the code (<see cref="ConfigurationPerDeploymentAttribute"/>, <see cref="AppConfiguration.AddPerDeploymentKey"/>,
/// <see cref="CentralProjectionContext.AddPerDeploymentKey"/>).
/// </para>
/// </summary>
public sealed class CentralDeployment
{
    /// <summary>
    /// The folder of the deployments, next to the central "ckonf.json": "Deployment".
    /// </summary>
    public const string FolderName = "Deployment";

    /// <summary>
    /// The file written next to the overlay of a deployment: its merged configuration.
    /// </summary>
    public const string MergedFileName = "ckonf.merged.json";

    /// <summary>
    /// The key of the overlay that names the deployment it overlays: "$BASE" (the central configuration without it).
    /// </summary>
    public const string BaseKey = "$BASE";

    /// <summary>
    /// The key of the markers of the values per deployment: "$PER_DEPLOYMENT" (an array of patterns).
    /// </summary>
    public const string PerDeploymentKey = "$PER_DEPLOYMENT";

    static readonly JsonSerializerOptions _writeOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    readonly JsonObject _overlay;

    CentralDeployment( string name,
                       NormalizedPath folder,
                       CentralDeployment? baseDeployment,
                       JsonObject overlay,
                       JsonObject merged,
                       CentralConfiguration configuration,
                       IReadOnlyList<PerDeploymentMarker> markers )
    {
        Name = name;
        Folder = folder;
        Base = baseDeployment;
        _overlay = overlay;
        Merged = merged;
        Configuration = configuration;
        Markers = markers;
    }

    /// <summary>
    /// Gets the name of the deployment: its path in the "Deployment" folder ("#Production", "Paris/Shop/#Staging").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the folder of the deployment.
    /// </summary>
    public NormalizedPath Folder { get; }

    /// <summary>
    /// Gets the deployment that this one overlays (its <c>"$BASE"</c>). Null when it overlays the central configuration.
    /// </summary>
    public CentralDeployment? Base { get; }

    /// <summary>
    /// Gets the merged configuration (without its <c>"$PER_DEPLOYMENT"</c> markers).
    /// </summary>
    public JsonObject Merged { get; }

    /// <summary>
    /// Gets the merged configuration as a central configuration.
    /// </summary>
    public CentralConfiguration Configuration { get; }

    /// <summary>
    /// Gets the <c>"$PER_DEPLOYMENT"</c> markers that apply to this deployment: the ones of the central configuration, of
    /// its bases and its own.
    /// </summary>
    public IReadOnlyList<PerDeploymentMarker> Markers { get; }

    /// <inheritdoc />
    public override string ToString() => $"Deployment '{Name}'";

    /// <summary>
    /// Reads the deployments of a central configuration (none when there is no "Deployment" folder).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="central">The central configuration.</param>
    /// <param name="deployments">The deployments, sorted by name.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public static bool TryLoad( IActivityMonitor monitor, CentralConfiguration central, out IReadOnlyList<CentralDeployment> deployments )
    {
        deployments = [];
        var root = central.RootFolder.AppendPart( FolderName );
        if( !Directory.Exists( root ) ) return true;
        using var group = monitor.OpenInfo( $"Reading the deployments of '{root}'." );
        var found = new List<(string Name, NormalizedPath Folder, string? Domain, string Environment)>();
        bool success = Discover( monitor, central, root, root, [], found );
        // The raw overlays.
        var raws = new Dictionary<string, Raw>( StringComparer.OrdinalIgnoreCase );
        foreach( var (name, folder, domain, environment) in found )
        {
            var file = folder.AppendPart( CentralConfiguration.FileName );
            if( !File.Exists( file ) )
            {
                monitor.Error( $"The deployment folder '{folder}' has no '{CentralConfiguration.FileName}'." );
                success = false;
                continue;
            }
            if( !CkonfReader.TryRead( monitor, file, out var overlay, out _ ) )
            {
                success = false;
                continue;
            }
            var markers = new List<PerDeploymentMarker>();
            if( !PerDeploymentMarker.Extract( monitor, overlay, "", $"'{file}'", markers ) )
            {
                success = false;
                continue;
            }
            string? baseName = null;
            foreach( var (key, value) in overlay.ToList() )
            {
                if( key.Equals( BaseKey, StringComparison.OrdinalIgnoreCase ) )
                {
                    if( value is not JsonValue v || !v.TryGetValue<string>( out var b ) || string.IsNullOrWhiteSpace( b ) )
                    {
                        monitor.Error( $"Invalid \"{BaseKey}\" in '{file}': the name of a deployment (\"#Production\") is expected." );
                        success = false;
                    }
                    else baseName = b.Replace( '\\', '/' ).Trim().Trim( '/' );
                    overlay.Remove( key );
                }
                else if( key.Equals( "Domain", StringComparison.OrdinalIgnoreCase ) || key.Equals( "Environment", StringComparison.OrdinalIgnoreCase ) )
                {
                    monitor.Error( $"\"{key}\" can't be written in '{file}': the domain and the environment of a deployment are its path in the \"{FolderName}\" folder." );
                    success = false;
                }
                else if( key.Equals( "GlobalDomainPrefix", StringComparison.OrdinalIgnoreCase ) )
                {
                    monitor.Error( $"\"{key}\" can't be written in '{file}': it is how the solution names its projects, written in its central configuration only." );
                    success = false;
                }
            }
            raws.Add( name, new Raw( name, folder, domain, environment, baseName, overlay, markers ) );
        }
        if( !success ) return false;
        var resolved = new Dictionary<string, CentralDeployment>( StringComparer.OrdinalIgnoreCase );
        foreach( var r in raws.Values.OrderBy( r => r.Name, StringComparer.OrdinalIgnoreCase ) )
        {
            success &= Resolve( monitor, central, r, raws, resolved, [] ) != null;
        }
        if( !success ) return false;
        deployments = resolved.Values.OrderBy( d => d.Name, StringComparer.OrdinalIgnoreCase ).ToList();
        monitor.Info( $"{deployments.Count} deployment(s): '{deployments.Select( d => d.Name ).Concatenate( "', '" )}'." );
        return true;
    }

    sealed record Raw( string Name,
                       NormalizedPath Folder,
                       string? Domain,
                       string Environment,
                       string? BaseName,
                       JsonObject Overlay,
                       List<PerDeploymentMarker> Markers );

    static bool Discover( IActivityMonitor monitor,
                          CentralConfiguration central,
                          NormalizedPath root,
                          NormalizedPath folder,
                          List<string> parts,
                          List<(string, NormalizedPath, string?, string)> found )
    {
        bool success = true;
        foreach( var d in Directory.EnumerateDirectories( folder ).Order( StringComparer.OrdinalIgnoreCase ) )
        {
            var name = Path.GetFileName( d );
            if( name.StartsWith( '.' ) ) continue;
            if( parts.Count == 0 && name.Equals( "Assets", StringComparison.OrdinalIgnoreCase ) ) continue;
            if( name.StartsWith( '#' ) )
            {
                if( name.Length == 1 || name.Equals( "#Dev", StringComparison.OrdinalIgnoreCase ) || name.Equals( "#Development", StringComparison.OrdinalIgnoreCase ) )
                {
                    monitor.Error( $"Invalid deployment folder '{d}': the environment of a deployment is a name after the '#', and the development (\"#Dev\") is the central configuration." );
                    success = false;
                    continue;
                }
                string? domain;
                if( parts.Count == 0 )
                {
                    domain = central.Configuration["Domain"];
                }
                else
                {
                    domain = string.Join( '/', parts );
                    if( parts.Any( p => p.Contains( '.' ) ) )
                    {
                        monitor.Error( $"Invalid deployment folder '{d}': a part of a domain has no '.'." );
                        success = false;
                        continue;
                    }
                    if( domain.Equals( central.Configuration["Domain"], StringComparison.OrdinalIgnoreCase ) )
                    {
                        monitor.Error( $"The deployment folder '{d}' spells the central domain '{domain}': it is '{root.AppendPart( name )}'." );
                        success = false;
                        continue;
                    }
                }
                var deploymentName = parts.Count == 0 ? name : $"{string.Join( '/', parts )}/{name}";
                found.Add( (deploymentName, new NormalizedPath( d ), domain, name) );
            }
            else
            {
                success &= Discover( monitor, central, root, new NormalizedPath( d ), [.. parts, name], found );
            }
        }
        return success;
    }

    static CentralDeployment? Resolve( IActivityMonitor monitor,
                                       CentralConfiguration central,
                                       Raw raw,
                                       Dictionary<string, Raw> raws,
                                       Dictionary<string, CentralDeployment> resolved,
                                       List<string> stack )
    {
        if( resolved.TryGetValue( raw.Name, out var done ) ) return done;
        if( stack.Contains( raw.Name, StringComparer.OrdinalIgnoreCase ) )
        {
            monitor.Error( $"The deployments '{stack.Concatenate( "' -> '" )}' -> '{raw.Name}' overlay each other (\"{BaseKey}\")." );
            return null;
        }
        CentralDeployment? baseDeployment = null;
        if( raw.BaseName != null )
        {
            if( !raws.TryGetValue( raw.BaseName, out var baseRaw ) )
            {
                monitor.Error( $"The \"{BaseKey}\" of the deployment '{raw.Name}' is '{raw.BaseName}': there is no such deployment ('{raws.Keys.Concatenate( "', '" )}')." );
                return null;
            }
            stack.Add( raw.Name );
            baseDeployment = Resolve( monitor, central, baseRaw, raws, resolved, stack );
            stack.RemoveAt( stack.Count - 1 );
            if( baseDeployment == null ) return null;
        }
        var merged = (JsonObject)(baseDeployment?.Merged ?? central.RootWithoutMarkers).DeepClone();
        merged = (JsonObject)MergePatch( merged, raw.Overlay )!;
        if( raw.Domain != null ) merged["Domain"] = raw.Domain;
        merged["Environment"] = raw.Environment;
        var file = raw.Folder.AppendPart( CentralConfiguration.FileName );
        var configuration = CentralConfiguration.Parse( monitor, file, merged.ToJsonString() );
        if( configuration == null )
        {
            monitor.Error( $"The merged configuration of the deployment '{raw.Name}' is invalid." );
            return null;
        }
        var markers = new List<PerDeploymentMarker>( baseDeployment?.Markers ?? central.PerDeploymentMarkers );
        markers.AddRange( raw.Markers );
        var d = new CentralDeployment( raw.Name, raw.Folder, baseDeployment, raw.Overlay, merged, configuration, markers );
        resolved.Add( raw.Name, d );
        return d;
    }

    /// <summary>
    /// Applies a JSON Merge Patch (RFC 7396, the keys are case insensitive): an object merges, <c>null</c> removes, any
    /// other value (an array included) replaces.
    /// </summary>
    /// <param name="target">The target (it is modified when it is an object and the patch is an object).</param>
    /// <param name="patch">The patch.</param>
    /// <returns>The result.</returns>
    public static JsonNode? MergePatch( JsonNode? target, JsonNode? patch )
    {
        if( patch is not JsonObject p ) return patch?.DeepClone();
        var t = target as JsonObject ?? new JsonObject( new JsonNodeOptions { PropertyNameCaseInsensitive = true } );
        foreach( var (key, value) in p )
        {
            var existingKey = t.Select( kv => kv.Key ).FirstOrDefault( k => k.Equals( key, StringComparison.OrdinalIgnoreCase ) );
            if( value == null )
            {
                if( existingKey != null ) t.Remove( existingKey );
                continue;
            }
            JsonNode? current = null;
            if( existingKey != null )
            {
                current = t[existingKey];
                t.Remove( existingKey );
            }
            t[existingKey ?? key] = MergePatch( current, value );
        }
        return t;
    }

    /// <summary>
    /// Writes the merged configuration (<see cref="MergedFileName"/>) next to the overlay, when it changes: several
    /// builders of a solution may write it at the same time (the same content).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="central">The central configuration.</param>
    /// <returns>True on success, false on error.</returns>
    public bool WriteMergedFile( IActivityMonitor monitor, CentralConfiguration central )
    {
        var sources = new List<string>();
        for( var d = this; d != null; d = d.Base ) sources.Insert( 0, $"{FolderName}/{d.Name}/{CentralConfiguration.FileName}" );
        sources.Insert( 0, CentralConfiguration.FileName );
        var text = $"// Generated from {sources.Concatenate( ", " )} (from the root of the solution): do not edit.{Environment.NewLine}"
                   + Merged.ToJsonString( _writeOptions )
                   + Environment.NewLine;
        var file = Folder.AppendPart( MergedFileName );
        try
        {
            if( File.Exists( file ) && File.ReadAllText( file ) == text ) return true;
            var temp = Folder.AppendPart( $"{MergedFileName}.{Guid.NewGuid():N}.tmp" );
            File.WriteAllText( temp, text );
            File.Move( temp, file, overwrite: true );
            monitor.Info( $"'{file}' written." );
            return true;
        }
        catch( IOException ex )
        {
            // Another builder wrote it at the same time.
            if( File.Exists( file ) && File.ReadAllText( file ) == text ) return true;
            monitor.Error( $"While writing '{file}'.", ex );
            return false;
        }
    }

    /// <summary>
    /// Checks the values per deployment: the <see cref="Markers"/>, the central keys declared by the contributors
    /// (<see cref="CentralConfiguration.PerDeploymentKeys"/>) and the App keys of a builder (declared by the code, relative
    /// to an App: <see cref="AppConfiguration.AddPerDeploymentKey"/>, <see cref="ConfigurationPerDeploymentAttribute"/>).
    /// A value that exists in the <see cref="Merged"/> configuration and that this deployment doesn't write itself is an
    /// error.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="central">The central configuration.</param>
    /// <param name="builderFolder">The folder of the builder whose App keys are given.</param>
    /// <param name="appKeys">The keys per deployment of the Apps of this builder, by App name: the patterns relative to the App and their source.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public bool CheckPerDeployment( IActivityMonitor monitor,
                                    CentralConfiguration central,
                                    NormalizedPath builderFolder,
                                    IReadOnlyDictionary<string, IReadOnlyList<(string Pattern, string Source)>> appKeys )
    {
        var reported = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        bool success = true;
        foreach( var m in Markers )
        {
            success &= Check( monitor, m.ObjectPath, m.Pattern, $"\"{PerDeploymentKey}\" of {m.Source}", reported );
        }
        foreach( var (key, source) in central.PerDeploymentKeys )
        {
            success &= Check( monitor, "", key, source, reported );
        }
        // The builder in the deployment (the root of its configuration is its folder: the builders are found by path).
        var builderPath = central.FindBuilder( builderFolder )?.Path;
        var builder = Configuration.Builders.FirstOrDefault( b => b.Path.Equals( builderPath, StringComparison.OrdinalIgnoreCase ) );
        if( builder != null )
        {
            foreach( var app in builder.Apps )
            {
                if( !appKeys.TryGetValue( app.Name, out var keys ) ) continue;
                var appPath = RelativePath( app.Configuration.Path );
                foreach( var (pattern, source) in keys )
                {
                    success &= CheckApp( monitor, appPath, pattern, source, reported );
                }
            }
        }
        return success;
    }

    static string RelativePath( string sectionPath )
    {
        var i = sectionPath.IndexOf( ':' );
        return i < 0 ? "" : sectionPath[(i + 1)..];
    }

    bool Check( IActivityMonitor monitor, string objectPath, string pattern, string source, HashSet<string> reported )
    {
        if( !ConfigurationPathPattern.TryParse( pattern, out var p, out var error ) )
        {
            monitor.Error( $"Invalid value per deployment '{pattern}' ({source}): {error}." );
            return false;
        }
        bool success = true;
        foreach( var leaf in Leaves( Find( Merged, objectPath ), "" ) )
        {
            if( !p.Covers( leaf ) ) continue;
            success &= CheckWritten( monitor, Combine( objectPath, leaf ), source, reported );
        }
        return success;
    }

    bool CheckApp( IActivityMonitor monitor, string appPath, string pattern, string source, HashSet<string> reported )
    {
        if( !ConfigurationPathPattern.TryParse( pattern, out var p, out var error ) )
        {
            monitor.Error( $"Invalid value per deployment '{pattern}' ({source}): {error}." );
            return false;
        }
        bool success = true;
        var app = Find( Merged, appPath );
        foreach( var leaf in Leaves( app, "" ) )
        {
            if( p.Covers( leaf ) ) success &= CheckWritten( monitor, Combine( appPath, leaf ), source, reported );
        }
        // The values that the App takes from the "Defaults:App" of the central configuration.
        foreach( var leaf in Leaves( Find( Merged, "Defaults:App" ), "" ) )
        {
            if( p.Covers( leaf ) && (app == null || Find( (JsonObject)app, leaf ) == null) )
            {
                success &= CheckWritten( monitor, $"Defaults:App:{leaf}", source, reported );
            }
        }
        return success;
    }

    bool CheckWritten( IActivityMonitor monitor, string path, string source, HashSet<string> reported )
    {
        if( IsWritten( _overlay, path ) || !reported.Add( path ) ) return true;
        monitor.Error( $"The deployment '{Name}' inherits \"{path}\" ({source}): a value per deployment is written by the deployment itself, in '{Folder.AppendPart( CentralConfiguration.FileName )}' (null removes it)." );
        return false;
    }

    // Written by the overlay: the overlay holds the path, or a prefix of it whose value is not an object (a value, an
    // array or null: the merge patch replaces it whole).
    static bool IsWritten( JsonObject overlay, string path )
    {
        JsonNode? node = overlay;
        foreach( var segment in path.Split( ':' ) )
        {
            if( node is not JsonObject o ) return false;
            var key = o.Select( kv => kv.Key ).FirstOrDefault( k => k.Equals( segment, StringComparison.OrdinalIgnoreCase ) );
            if( key == null ) return false;
            node = o[key];
            if( node is not JsonObject ) return true;
        }
        return false;
    }

    static JsonNode? Find( JsonNode? root, string path )
    {
        if( path.Length == 0 ) return root;
        var node = root;
        foreach( var segment in path.Split( ':' ) )
        {
            if( node is JsonObject o )
            {
                var key = o.Select( kv => kv.Key ).FirstOrDefault( k => k.Equals( segment, StringComparison.OrdinalIgnoreCase ) );
                if( key == null ) return null;
                node = o[key];
            }
            else if( node is JsonArray a && int.TryParse( segment, out var i ) && i >= 0 && i < a.Count )
            {
                node = a[i];
            }
            else return null;
        }
        return node;
    }

    static IEnumerable<string> Leaves( JsonNode? node, string prefix )
    {
        switch( node )
        {
            case JsonObject o:
                foreach( var (key, value) in o )
                {
                    foreach( var l in Leaves( value, Combine( prefix, key ) ) ) yield return l;
                }
                break;
            case JsonArray a:
                for( int i = 0; i < a.Count; ++i )
                {
                    foreach( var l in Leaves( a[i], Combine( prefix, i.ToString( System.Globalization.CultureInfo.InvariantCulture ) ) ) ) yield return l;
                }
                break;
            case JsonValue:
                yield return prefix;
                break;
        }
    }

    static string Combine( string a, string b ) => a.Length == 0 ? b : $"{a}:{b}";
}

/// <summary>
/// A <c>"$PER_DEPLOYMENT"</c> marker: a pattern of values per deployment, relative to the object that holds it.
/// </summary>
/// <param name="ObjectPath">The path of the object that holds the marker in the central configuration ("" for the root).</param>
/// <param name="Pattern">The pattern, relative to the object.</param>
/// <param name="Source">The file that holds it (for the messages).</param>
public sealed record PerDeploymentMarker( string ObjectPath, string Pattern, string Source )
{
    /// <summary>
    /// Extracts the <c>"$PER_DEPLOYMENT"</c> markers of a configuration (they are removed from it): build inputs, like the
    /// <c>"$LOCK"</c>, never projected.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="node">The object to explore.</param>
    /// <param name="path">The path of the object.</param>
    /// <param name="source">The file (for the messages).</param>
    /// <param name="markers">Collects the markers.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool Extract( IActivityMonitor monitor, JsonObject node, string path, string source, List<PerDeploymentMarker> markers )
    {
        bool success = true;
        foreach( var (key, value) in node.ToList() )
        {
            var childPath = path.Length == 0 ? key : $"{path}:{key}";
            if( key.Equals( CentralDeployment.PerDeploymentKey, StringComparison.OrdinalIgnoreCase ) )
            {
                node.Remove( key );
                if( value is not JsonArray a || a.Any( i => i is not JsonValue v || !v.TryGetValue<string>( out _ ) ) )
                {
                    monitor.Error( $"Invalid \"{childPath}\" in {source}: an array of patterns (strings) is expected." );
                    success = false;
                    continue;
                }
                foreach( var pattern in a.Select( i => i!.GetValue<string>() ) )
                {
                    if( !ConfigurationPathPattern.TryParse( pattern, out _, out var error ) )
                    {
                        monitor.Error( $"Invalid pattern '{pattern}' in \"{childPath}\" of {source}: {error}." );
                        success = false;
                    }
                    else markers.Add( new PerDeploymentMarker( path, pattern, source ) );
                }
            }
            else if( value is JsonObject o )
            {
                success &= Extract( monitor, o, childPath, source, markers );
            }
            else if( value is JsonArray array )
            {
                for( int i = 0; i < array.Count; ++i )
                {
                    if( array[i] is JsonObject item ) success &= Extract( monitor, item, $"{childPath}:{i}", source, markers );
                }
            }
        }
        return success;
    }
}
