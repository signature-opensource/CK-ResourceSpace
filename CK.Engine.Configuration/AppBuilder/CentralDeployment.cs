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

    /// <summary>
    /// The root key of the settings of the deployments (see <see cref="CentralDeploymentSettings"/>): "Deployment", in the
    /// central configuration (the defaults of every deployment) or in an overlay.
    /// </summary>
    public const string SettingsKey = "Deployment";

    /// <summary>
    /// The file that makes a Builder the Builder of a deployment: <c>{ "Deployment": "#Production", "AppContext": "Front" }</c>,
    /// written in the generated Builder of a deployment (see <see cref="WriteBuilderProjection"/>).
    /// </summary>
    public const string BuilderFileName = "ckonf.deployment.json";

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
                       IReadOnlyList<PerDeploymentMarker> markers,
                       CentralDeploymentSettings settings )
    {
        Name = name;
        Folder = folder;
        Base = baseDeployment;
        _overlay = overlay;
        Merged = merged;
        Configuration = configuration;
        Markers = markers;
        Settings = settings;
    }

    /// <summary>
    /// Gets the settings of this deployment that are not configuration: its builders, how its hosts are published.
    /// </summary>
    public CentralDeploymentSettings Settings { get; }

    /// <summary>
    /// Gets the environment of this deployment: the name of its folder ("#Production").
    /// </summary>
    public string Environment => Folder.LastPart;

    /// <summary>
    /// Gets the name of the assets of this deployment (a release asset name has no '#'): the <see cref="Name"/> without
    /// its '#', its '/' mapped to '.' ("Production", "Paris.Shop.Staging"). Reversible: the environment is the last part
    /// and a part of a domain has no '.'.
    /// </summary>
    public string AssetName => Name.Replace( "#", "" ).Replace( '/', '.' );

    /// <summary>
    /// Gets the folder of the published host of an App in the assets (CKli zips each folder of the assets): by default,
    /// one asset per App, "{AssetName}-{ProjectName}" ("Production-Auth3.Front"); with <see cref="CentralDeploymentSettings.SingleZipAsset"/>,
    /// one asset for the deployment, "{AssetName}/{ProjectName}" ("Production/Auth3.Front").
    /// </summary>
    /// <param name="app">The App.</param>
    /// <returns>The relative path of the folder.</returns>
    public string GetAssetFolder( CentralApp app ) => Settings.SingleZipAsset ? $"{AssetName}/{app.ProjectName}" : $"{AssetName}-{app.ProjectName}";

    /// <summary>
    /// Gets the AppContexts that this deployment builds (<see cref="CentralDeploymentSettings.AppContexts"/>, all by
    /// default), in the order of its configuration.
    /// </summary>
    public IEnumerable<CentralAppContext> DeployedAppContexts
        => Configuration.AppContexts.Where( c => Settings.AppContexts == null || Settings.AppContexts.Contains( c.Name, StringComparer.OrdinalIgnoreCase ) );

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
        bool success = true;
        // The raw overlays.
        var raws = new Dictionary<string, Raw>( StringComparer.OrdinalIgnoreCase );
        foreach( var (name, folder, domain, environment, folderError) in DiscoverFolders( central.RootFolder, central.Configuration["Domain"] ) )
        {
            if( folderError != null )
            {
                monitor.Error( $"Invalid deployment folder '{folder}': {folderError}" );
                success = false;
                continue;
            }
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

    /// <summary>
    /// Finds the deployment folders of a solution (see <see cref="CentralDeployment"/>): the folders whose name starts with
    /// '#' in its "Deployment" folder, sorted by path. "Deployment/Assets" and the folders whose name starts with '.' are
    /// ignored. An invalid one (see <see cref="ValidateName"/>) is returned with its error.
    /// </summary>
    /// <param name="solutionFolder">The folder of the central "ckonf.json".</param>
    /// <param name="centralDomain">The "Domain" of the central configuration.</param>
    /// <returns>The deployment folders.</returns>
    public static IReadOnlyList<DeploymentFolder> DiscoverFolders( NormalizedPath solutionFolder, string? centralDomain )
    {
        var found = new List<DeploymentFolder>();
        var root = solutionFolder.AppendPart( FolderName );
        if( Directory.Exists( root ) ) Discover( root, [], centralDomain, found );
        return found;

        static void Discover( NormalizedPath folder, List<string> parts, string? centralDomain, List<DeploymentFolder> found )
        {
            foreach( var d in Directory.EnumerateDirectories( folder ).Order( StringComparer.OrdinalIgnoreCase ) )
            {
                var name = Path.GetFileName( d );
                if( name.StartsWith( '.' ) ) continue;
                if( parts.Count == 0 && name.Equals( "Assets", StringComparison.OrdinalIgnoreCase ) ) continue;
                if( name.StartsWith( '#' ) )
                {
                    var deploymentName = parts.Count == 0 ? name : $"{string.Join( '/', parts )}/{name}";
                    var error = ValidateName( deploymentName, centralDomain, out var domain, out var environment );
                    found.Add( new DeploymentFolder( deploymentName, new NormalizedPath( d ), domain, environment, error ) );
                }
                else
                {
                    Discover( new NormalizedPath( d ), [.. parts, name], centralDomain, found );
                }
            }
        }
    }

    /// <summary>
    /// Validates the name of a deployment: its path in the "Deployment" folder, the environment ('#' and a name, never
    /// "#Dev" or "#Development": the development is the central configuration) after the parts of its domain ("#Production",
    /// "Paris/Shop/#Staging"). A part of a domain has no '.' and doesn't start with '#' or '.', the first one is not
    /// "Assets", and a domain is not the central one (its deployments are directly in the "Deployment" folder).
    /// </summary>
    /// <param name="name">The name to validate.</param>
    /// <param name="centralDomain">The "Domain" of the central configuration.</param>
    /// <param name="domain">The domain of the deployment (the central one when the name has no domain part).</param>
    /// <param name="environment">The environment of the deployment ("#Production").</param>
    /// <returns>Null when the name is valid, the error otherwise.</returns>
    public static string? ValidateName( string name, string? centralDomain, out string? domain, out string environment )
    {
        Throw.CheckNotNullArgument( name );
        var parts = name.Replace( '\\', '/' ).Split( '/' );
        environment = parts[^1];
        domain = centralDomain;
        if( environment.Length < 2
            || environment[0] != '#'
            || environment.Equals( "#Dev", StringComparison.OrdinalIgnoreCase )
            || environment.Equals( "#Development", StringComparison.OrdinalIgnoreCase ) )
        {
            return "the environment of a deployment is a name after the '#', and the development (\"#Dev\") is the central configuration.";
        }
        if( environment.IndexOfAny( ['#', '.'], 1 ) >= 0 || environment.AsSpan( 1 ).IndexOfAny( Path.GetInvalidFileNameChars() ) >= 0 )
        {
            return $"the environment '{environment}' is a name after the '#' (no other '#', no '.').";
        }
        if( parts.Length == 1 ) return null;
        var domainParts = parts[..^1];
        if( domainParts.Any( p => p.Length == 0 || p[0] == '#' || p[0] == '.' || p.AsSpan().IndexOfAny( Path.GetInvalidFileNameChars() ) >= 0 ) )
        {
            return "a part of a domain is a name that doesn't start with '#' or '.'.";
        }
        if( domainParts[0].Equals( "Assets", StringComparison.OrdinalIgnoreCase ) )
        {
            return "\"Deployment/Assets\" is the folder of the assets, not a domain.";
        }
        if( domainParts.Any( p => p.Contains( '.' ) ) )
        {
            return "a part of a domain has no '.'.";
        }
        domain = string.Join( '/', domainParts );
        if( domain.Equals( centralDomain, StringComparison.OrdinalIgnoreCase ) )
        {
            return $"it spells the central domain '{domain}': its deployments are directly in the \"{FolderName}\" folder ('{FolderName}/{environment}').";
        }
        return null;
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
        var settings = CentralDeploymentSettings.Read( monitor, merged[SettingsKey], configuration, $"the deployment '{raw.Name}'" );
        if( settings == null ) return null;
        var d = new CentralDeployment( raw.Name, raw.Folder, baseDeployment, raw.Overlay, merged, configuration, markers, settings );
        resolved.Add( raw.Name, d );
        return d;
    }

    /// <summary>
    /// Writes the "ckonf.json" of the Builder of this deployment generated for one of the AppContexts of the solution: the
    /// projection of the AppContext in the merged configuration, its Apps on their original ".App" project ("AppProject":
    /// the generated Builder references it), their ".Host" project the copy next to the generated Builder (named after the
    /// original one).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="central">The central configuration (where the original projects are).</param>
    /// <param name="appContextName">The name of the AppContext.</param>
    /// <param name="folder">The folder of the generated Builder.</param>
    /// <param name="contributors">The contributors, in their order.</param>
    /// <returns>True on success, false on error.</returns>
    public bool WriteBuilderProjection( IActivityMonitor monitor,
                                        CentralConfiguration central,
                                        string appContextName,
                                        NormalizedPath folder,
                                        IEnumerable<IAppBuilderContributor> contributors )
    {
        var appContext = Configuration.FindAppContext( appContextName );
        var original = central.FindAppContext( appContextName );
        if( appContext == null || original == null )
        {
            monitor.Error( $"The AppContext '{appContextName}' is not an AppContext of the deployment '{Name}'." );
            return false;
        }
        bool success = true;
        var r = Configuration.WriteProjection( monitor,
                                               appContext,
                                               contributors,
                                               folder,
                                               $"{FolderName}/{Name}/{MergedFileName} (the deployment '{Name}')",
                                               ( app, node ) =>
                                               {
                                                   var o = original.Apps.FirstOrDefault( a => a.Name.Equals( app.Name, StringComparison.OrdinalIgnoreCase ) );
                                                   if( o == null )
                                                   {
                                                       monitor.Error( $"The {app} of the deployment '{Name}' is not an App of the AppContext '{appContextName}' of the solution." );
                                                       success = false;
                                                       return;
                                                   }
                                                   node.Remove( "AppProject" );
                                                   node.Remove( "HostProject" );
                                                   node["AppProject"] = Path.GetRelativePath( folder, o.AppProjectFolder ).Replace( '\\', '/' );
                                                   node["HostProject"] = $"../{o.HostProjectFolder.LastPart}";
                                               } );
        return r && success;
    }

    /// <summary>
    /// Reads the <see cref="BuilderFileName"/> of a Builder, if it exists.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="builderFolder">The folder of the Builder.</param>
    /// <param name="deploymentName">The name of the deployment, null when the file doesn't exist.</param>
    /// <param name="appContextName">The name of the AppContext of the solution that this Builder builds for the deployment.</param>
    /// <returns>True on success (with or without the file), false on error.</returns>
    public static bool TryReadBuilderFile( IActivityMonitor monitor, NormalizedPath builderFolder, out string? deploymentName, out string? appContextName )
    {
        deploymentName = null;
        appContextName = null;
        var file = builderFolder.AppendPart( BuilderFileName );
        if( !File.Exists( file ) ) return true;
        try
        {
            var o = JsonNode.Parse( File.ReadAllText( file ), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip } ) as JsonObject;
            deploymentName = (o?["Deployment"] as JsonValue)?.GetValue<string>();
            appContextName = (o?["AppContext"] as JsonValue)?.GetValue<string>();
        }
        catch( Exception ex )
        {
            monitor.Error( $"While reading '{file}'.", ex );
            return false;
        }
        if( string.IsNullOrWhiteSpace( deploymentName ) || string.IsNullOrWhiteSpace( appContextName ) )
        {
            monitor.Error( $"Invalid '{file}': {{ \"Deployment\": \"#Production\", \"AppContext\": \"Name\" }} is expected." );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Writes the <see cref="BuilderFileName"/> in the folder of a Builder generated for this deployment.
    /// </summary>
    /// <param name="builderFolder">The folder of the generated Builder.</param>
    /// <param name="appContextName">The name of the AppContext of the solution that it builds for this deployment.</param>
    public void WriteBuilderFile( NormalizedPath builderFolder, string appContextName )
    {
        var o = new JsonObject { ["Deployment"] = Name, ["AppContext"] = appContextName };
        File.WriteAllText( builderFolder.AppendPart( BuilderFileName ),
                           $"// Written by CK.AppDeployment: this Builder builds the AppContext '{appContextName}' for the deployment '{Name}'.{System.Environment.NewLine}"
                           + o.ToJsonString( _writeOptions ) + System.Environment.NewLine );
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
        var text = $"// Generated from {sources.Concatenate( ", " )} (from the root of the solution): do not edit.{System.Environment.NewLine}"
                   + Merged.ToJsonString( _writeOptions )
                   + System.Environment.NewLine;
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
    /// (<see cref="CentralConfiguration.PerDeploymentKeys"/>) and the App keys of an AppContext (declared by the code, relative
    /// to an App: <see cref="AppConfiguration.AddPerDeploymentKey"/>, <see cref="ConfigurationPerDeploymentAttribute"/>).
    /// A value that exists in the <see cref="Merged"/> configuration and that this deployment doesn't write itself is an
    /// error.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="central">The central configuration.</param>
    /// <param name="appContextName">The name of the AppContext whose App keys are given.</param>
    /// <param name="appKeys">The keys per deployment of the Apps of this AppContext, by App name: the patterns relative to the App and their source.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public bool CheckPerDeployment( IActivityMonitor monitor,
                                    CentralConfiguration central,
                                    string appContextName,
                                    IReadOnlyDictionary<string, IReadOnlyList<(string Pattern, string Source)>> appKeys )
    {
        var patterns = new List<PerDeploymentPattern>();
        foreach( var m in Markers )
        {
            patterns.Add( new PerDeploymentPattern( m.ObjectPath, m.Pattern, $"\"{PerDeploymentKey}\" of {m.Source}" ) );
        }
        foreach( var (key, source) in central.PerDeploymentKeys )
        {
            patterns.Add( new PerDeploymentPattern( "", key, source ) );
        }
        // The AppContext in the deployment.
        var appContext = Configuration.FindAppContext( appContextName );
        if( appContext != null )
        {
            foreach( var app in appContext.Apps )
            {
                if( !appKeys.TryGetValue( app.Name, out var keys ) ) continue;
                var appPath = RelativePath( app.Configuration.Path );
                foreach( var (pattern, source) in keys )
                {
                    patterns.Add( new PerDeploymentPattern( appPath, pattern, source, IsApp: true ) );
                }
            }
        }
        var inherited = FindInheritedValues( Merged, _overlay, patterns, out var invalidPatterns );
        foreach( var e in invalidPatterns ) monitor.Error( e );
        foreach( var v in inherited )
        {
            monitor.Error( $"The deployment '{Name}' inherits \"{v.Path}\" ({v.Source}): a value per deployment is written by the deployment itself, in '{Folder.AppendPart( CentralConfiguration.FileName )}' (null removes it)." );
        }
        return invalidPatterns.Count == 0 && inherited.Count == 0;
    }

    /// <summary>
    /// Finds the values per deployment that a deployment inherits instead of writing them (the rule of
    /// <see cref="CheckPerDeployment"/>, as data: the configurator checks the documents that it edits with it).
    /// A value is written by the overlay when the overlay holds its path, or a prefix of it whose value is not an object
    /// (a value, an array or null: the merge patch replaces it whole). A path is reported once, with the source of the
    /// first pattern that covers it.
    /// </summary>
    /// <param name="merged">The merged configuration of the deployment (without its markers).</param>
    /// <param name="overlay">The overlay of the deployment (its own "ckonf.json", without its markers).</param>
    /// <param name="patterns">The patterns of the values per deployment.</param>
    /// <param name="invalidPatterns">The messages of the invalid patterns (they are skipped).</param>
    /// <returns>The inherited values per deployment.</returns>
    public static IReadOnlyList<InheritedPerDeploymentValue> FindInheritedValues( JsonObject merged,
                                                                                  JsonObject overlay,
                                                                                  IEnumerable<PerDeploymentPattern> patterns,
                                                                                  out IReadOnlyList<string> invalidPatterns )
    {
        Throw.CheckNotNullArgument( merged );
        Throw.CheckNotNullArgument( overlay );
        Throw.CheckNotNullArgument( patterns );
        var reported = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        var result = new List<InheritedPerDeploymentValue>();
        var invalid = new List<string>();
        foreach( var (objectPath, pattern, source, isApp) in patterns )
        {
            if( !ConfigurationPathPattern.TryParse( pattern, out var p, out var error ) )
            {
                invalid.Add( $"Invalid value per deployment '{pattern}' ({source}): {error}." );
                continue;
            }
            var o = Find( merged, objectPath );
            foreach( var leaf in Leaves( o, "" ) )
            {
                if( p.Covers( leaf ) ) Add( Combine( objectPath, leaf ), source );
            }
            if( !isApp ) continue;
            // The values that the App takes from the "Defaults:App" of the central configuration.
            foreach( var leaf in Leaves( Find( merged, "Defaults:App" ), "" ) )
            {
                if( p.Covers( leaf ) && (o is not JsonObject app || Find( app, leaf ) == null) )
                {
                    Add( $"Defaults:App:{leaf}", source );
                }
            }
        }
        invalidPatterns = invalid;
        return result;

        void Add( string path, string source )
        {
            if( !IsWritten( overlay, path ) && reported.Add( path ) ) result.Add( new InheritedPerDeploymentValue( path, source ) );
        }
    }

    static string RelativePath( string sectionPath )
    {
        var i = sectionPath.IndexOf( ':' );
        return i < 0 ? "" : sectionPath[(i + 1)..];
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
/// A deployment folder found by <see cref="CentralDeployment.DiscoverFolders"/>.
/// </summary>
/// <param name="Name">The name of the deployment: its path in the "Deployment" folder ("#Production", "Paris/Shop/#Staging").</param>
/// <param name="Folder">The folder.</param>
/// <param name="Domain">The domain of the deployment.</param>
/// <param name="Environment">The environment ("#Production").</param>
/// <param name="Error">Null when the folder is a valid deployment, the error otherwise (see <see cref="CentralDeployment.ValidateName"/>).</param>
public sealed record DeploymentFolder( string Name, NormalizedPath Folder, string? Domain, string Environment, string? Error );

/// <summary>
/// A pattern of values per deployment (see <see cref="CentralDeployment.FindInheritedValues"/>).
/// </summary>
/// <param name="ObjectPath">The path of the object that the pattern is relative to ("" for the root).</param>
/// <param name="Pattern">The pattern (<see cref="ConfigurationPathPattern"/>).</param>
/// <param name="Source">Where the pattern comes from (for the messages).</param>
/// <param name="IsApp">
/// True when <see cref="ObjectPath"/> is an App: the values that the App takes from the <c>"Defaults:App"</c> of the
/// configuration are covered too.
/// </param>
public sealed record PerDeploymentPattern( string ObjectPath, string Pattern, string Source, bool IsApp = false );

/// <summary>
/// A value per deployment that a deployment inherits instead of writing it (see <see cref="CentralDeployment.FindInheritedValues"/>).
/// </summary>
/// <param name="Path">The path of the value in the merged configuration.</param>
/// <param name="Source">The source of the pattern that covers it.</param>
public sealed record InheritedPerDeploymentValue( string Path, string Source );

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
