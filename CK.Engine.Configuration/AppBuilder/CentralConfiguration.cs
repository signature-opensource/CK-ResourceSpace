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
/// The central configuration of a solution: the optional "ckonf.json" at the root of its repository (its git root).
/// It holds the domain of the solution ("Domain", "GlobalDomainPrefix": the prefix of the projects of its Apps), its
/// AppContexts keyed by their name (an AppContext is what one engine run builds: its Apps and its AppContext aspects;
/// its Builder, "{RootPath}/{Name}.Builder", manages it), the defaults of the Apps ("Defaults:App") and what the
/// contributors read ("Links"...: the graph of the parties). The "ckonf.json" of each Builder is a projection of its
/// AppContext (<see cref="WriteProjection"/>).
/// The deployments of the solution are overlays of it, in its "Deployment" folder (see <see cref="CentralDeployment"/>):
/// its <c>"$PER_DEPLOYMENT"</c> markers are removed when it is read (<see cref="PerDeploymentMarkers"/>).
/// <code>
/// {
///   "Domain": "Sample/Auth3",
///   "GlobalDomainPrefix": "Sample",
///   "Defaults": { "App": { "Logging": { ... } } },
///   "AppContexts": {
///     // The Builder is "PrimaryServer/PrimaryServer.Builder", the Apps are "PrimaryServer/Auth3.Auth3.App" (and ".Host")
///     // and "PrimaryServer/Auth3.Jobs.App".
///     "PrimaryServer": { "RootPath": "PrimaryServer", "Aspects": { ... }, "Apps": [ { "Name": "$Auth3", ... }, { "Name": "$Jobs", ... } ] },
///     // The Builder is "Worker/Worker.Builder", its single App is "$Worker" ("Worker/Auth3.Worker.App").
///     "Worker": { "RootPath": "Worker", "App": { "Local": { ... } } }
///   },
///   "Links": [ ... ]
/// }
/// </code>
/// </summary>
public sealed class CentralConfiguration
{
    /// <summary>
    /// The central configuration file name, at the root of the repository.
    /// </summary>
    public const string FileName = "ckonf.json";

    /// <summary>
    /// The root key that the configurator writes (the source of its catalog: <c>{ "World": "...", "Version": "..." }</c>).
    /// It is accepted and ignored by the builders.
    /// </summary>
    public const string ConfiguratorKey = "$configurator";

    /// <summary>
    /// The root key of the AppContexts: <c>"AppContexts": { "&lt;Name&gt;": { ... } }</c>.
    /// </summary>
    public const string AppContextsKey = "AppContexts";

    /// <summary>
    /// The root key of the domain of the solution (<c>"Sample/Auth3"</c>): the domain of its parties, and the prefix of
    /// the projects of its Apps (without its <see cref="GlobalDomainPrefixKey"/>).
    /// </summary>
    public const string DomainKey = "Domain";

    /// <summary>
    /// The root key that holds the prefix of the <see cref="DomainKey"/> that the names of the projects of the Apps leave
    /// out: with <c>"Domain": "Acme/Europe/Paris"</c> and <c>"GlobalDomainPrefix": "Acme/Europe"</c>, the App
    /// <c>"$Shop"</c> is the "Paris.Shop.App" and "Paris.Shop.Host" projects.
    /// </summary>
    public const string GlobalDomainPrefixKey = "GlobalDomainPrefix";

    /// <summary>
    /// The key of an AppContext that holds the folder of its projects (its Builder and its Apps), relative to the root of
    /// the solution: the root itself by default.
    /// </summary>
    public const string RootPathKey = "RootPath";

    static readonly JsonSerializerOptions _writeOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    readonly JsonObject _root;
    readonly JsonObject? _appDefaults;
    readonly IReadOnlyList<CentralAppContext> _appContexts;
    readonly IReadOnlyList<PerDeploymentMarker> _markers;
    readonly List<(string Key, string Source)> _perDeploymentKeys;

    CentralConfiguration( NormalizedPath file,
                          ImmutableConfigurationSection configuration,
                          JsonObject root,
                          string? domain,
                          string? relativeDomain,
                          JsonObject? appDefaults,
                          List<CentralAppContext> appContexts,
                          IReadOnlyList<PerDeploymentMarker> markers )
    {
        File = file;
        Configuration = configuration;
        _root = root;
        Domain = domain;
        RelativeDomain = relativeDomain;
        _appDefaults = appDefaults;
        _appContexts = appContexts;
        _markers = markers;
        _perDeploymentKeys = new List<(string, string)>();
        foreach( var c in appContexts ) c.Central = this;
    }

    /// <summary>
    /// Gets the domain of the solution ("Sample/Auth3"), null when it has none.
    /// </summary>
    public string? Domain { get; }

    /// <summary>
    /// Gets the prefix of the projects of the Apps: the <see cref="Domain"/> without its "GlobalDomainPrefix", '/' mapped
    /// to '.' ("Auth3"). Null when there is no domain, or when the domain is the global prefix itself.
    /// </summary>
    public string? RelativeDomain { get; }

    /// <summary>
    /// Gets the <c>"$PER_DEPLOYMENT"</c> markers of this configuration (removed from it when it is read).
    /// </summary>
    public IReadOnlyList<PerDeploymentMarker> PerDeploymentMarkers => _markers;

    /// <summary>
    /// Gets the patterns of the values per deployment (relative to the root) that the contributors declared while they
    /// projected a builder (<see cref="CentralProjectionContext.AddPerDeploymentKey(string)"/>), with their source.
    /// </summary>
    public IReadOnlyList<(string Key, string Source)> PerDeploymentKeys => _perDeploymentKeys;

    // The content without the "$PER_DEPLOYMENT" markers: the base of the deployments.
    internal JsonObject RootWithoutMarkers => _root;

    /// <summary>
    /// Gets the central configuration file.
    /// </summary>
    public NormalizedPath File { get; }

    /// <summary>
    /// Gets the root folder: the folder of the <see cref="File"/>, the root of the repository.
    /// </summary>
    public NormalizedPath RootFolder => File.RemoveLastPart();

    /// <summary>
    /// Gets the whole configuration: the contributors read their root keys in it ("Domain", "Links"...).
    /// </summary>
    public ImmutableConfigurationSection Configuration { get; }

    /// <summary>
    /// Gets a copy of the JSON of a root key (case insensitive): its values keep their JSON kind, unlike the
    /// <see cref="Configuration"/> (strings only).
    /// </summary>
    /// <param name="key">The root key.</param>
    /// <returns>A copy of its JSON, or null if the key doesn't exist (or is null).</returns>
    public JsonNode? GetJson( string key ) => _root[key]?.DeepClone();

    /// <summary>
    /// Gets the AppContexts, in the order of the file.
    /// </summary>
    public IReadOnlyList<CentralAppContext> AppContexts => _appContexts;

    /// <summary>
    /// Gets all the Apps of all the AppContexts.
    /// </summary>
    public IEnumerable<CentralApp> AllApps => _appContexts.SelectMany( c => c.Apps );

    /// <summary>
    /// Finds an AppContext by its name.
    /// </summary>
    /// <param name="name">The name (case insensitive).</param>
    /// <returns>The AppContext or null.</returns>
    public CentralAppContext? FindAppContext( string name )
        => _appContexts.FirstOrDefault( c => c.Name.Equals( name, StringComparison.OrdinalIgnoreCase ) );

    /// <summary>
    /// Finds the AppContext that a Builder manages, from the Builder's folder.
    /// </summary>
    /// <param name="builderFolder">The Builder's folder.</param>
    /// <returns>The AppContext or null.</returns>
    public CentralAppContext? FindAppContextOfBuilder( NormalizedPath builderFolder )
        => _appContexts.FirstOrDefault( c => c.BuilderFolder.Path.Equals( builderFolder.Path, StringComparison.OrdinalIgnoreCase ) );

    /// <summary>
    /// Finds the git root above (or at) a folder: the first folder that contains a ".git" (a folder, or a file for a
    /// worktree).
    /// </summary>
    /// <param name="folder">The starting folder.</param>
    /// <returns>The git root, or the empty path.</returns>
    public static NormalizedPath FindGitRoot( NormalizedPath folder )
    {
        for( var f = folder; !f.IsEmptyPath; f = f.RemoveLastPart() )
        {
            var git = f.AppendPart( ".git" );
            if( Directory.Exists( git ) || System.IO.File.Exists( git ) ) return f;
        }
        return default;
    }

    /// <summary>
    /// Loads the central configuration of the repository of a builder: the "ckonf.json" at its git root, if any.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="builderFolder">The builder's folder.</param>
    /// <param name="central">The central configuration, null if there is none (or on error).</param>
    /// <returns>True on success (with or without a central configuration), false on error.</returns>
    public static bool TryLoad( IActivityMonitor monitor, NormalizedPath builderFolder, out CentralConfiguration? central )
    {
        central = null;
        var root = FindGitRoot( builderFolder );
        if( root.IsEmptyPath || root.Path.Equals( builderFolder.Path, StringComparison.OrdinalIgnoreCase ) ) return true;
        var file = root.AppendPart( FileName );
        if( !System.IO.File.Exists( file ) ) return true;
        using var _ = monitor.OpenInfo( $"Reading the central configuration '{file}'." );
        if( !CkonfReader.TryRead( monitor, file, out var json, out var section ) ) return false;
        central = Create( monitor, file, json, section );
        return central != null;
    }

    /// <summary>
    /// Creates a central configuration from its content (for the tests).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="file">The file (its folder is the root of the builders' paths).</param>
    /// <param name="json">The content.</param>
    /// <returns>The central configuration, or null on error.</returns>
    public static CentralConfiguration? Parse( IActivityMonitor monitor, NormalizedPath file, string json )
    {
        if( !CkonfReader.TryParse( monitor, file, json, out var root, out var section ) ) return null;
        return Create( monitor, file, root, section );
    }

    static CentralConfiguration? Create( IActivityMonitor monitor, NormalizedPath file, JsonObject root, ImmutableConfigurationSection section )
    {
        // The "$PER_DEPLOYMENT" markers are a build input: they are removed before anything reads the configuration.
        var markers = new List<PerDeploymentMarker>();
        if( !PerDeploymentMarker.Extract( monitor, root, "", $"'{file}'", markers ) ) return null;
        if( markers.Count > 0 ) section = ImmutableConfigurationSection.CreateFromJson( section.Path, root.ToJsonString() );
        bool success = true;
        // The defaults of the Apps: "Defaults:App".
        JsonObject? appDefaults = null;
        var defaults = root["Defaults"];
        if( defaults != null )
        {
            if( defaults is not JsonObject oDefaults )
            {
                monitor.Error( $"\"Defaults\" in '{file}' must be an object." );
                success = false;
            }
            else
            {
                foreach( var (key, value) in oDefaults )
                {
                    if( key.Equals( "App", StringComparison.OrdinalIgnoreCase ) && value is JsonObject a )
                    {
                        appDefaults = a;
                    }
                    else
                    {
                        monitor.Error( $"Invalid \"Defaults:{key}\" in '{file}': \"Defaults\" has \"App\" (an object: the defaults of every App)." );
                        success = false;
                    }
                }
            }
        }
        // The domain of the solution: the domain of its parties and the prefix of the projects of its Apps.
        success &= ReadDomain( monitor, file, root, out var domain, out var relativeDomain );
        // The AppContexts, keyed by their name.
        if( root[AppContextsKey] is not JsonObject oContexts || oContexts.Count == 0 )
        {
            monitor.Error( $"'{file}' must define \"{AppContextsKey}\": {{ \"Name\": {{ ... }} }} (at least one AppContext, keyed by its name: its Builder is \"{{{RootPathKey}}}/{{Name}}.Builder\")." );
            return null;
        }
        var rootFolder = file.RemoveLastPart();
        var appContexts = new List<CentralAppContext>();
        var appNames = new Dictionary<string, CentralAppContext>( StringComparer.OrdinalIgnoreCase );
        var contextsSection = section.GetRequiredSection( AppContextsKey );
        foreach( var (key, value) in oContexts )
        {
            var name = key.Trim();
            if( !CkonfReader.IsValidAppContextName( name ) )
            {
                monitor.Error( $"Invalid AppContext name '{key}' in '{file}': letters, digits, '_', '-' and '.' (an AppContext is not a party: no '$')." );
                success = false;
                continue;
            }
            if( value is not JsonObject oContext )
            {
                monitor.Error( $"The AppContext '{key}' in '{file}' must be an object." );
                success = false;
                continue;
            }
            // The configuration keys can't contain ':' (a separator): the section is found by position.
            var cSection = contextsSection.GetChildren().FirstOrDefault( c => c.Key.Equals( key, StringComparison.OrdinalIgnoreCase ) )
                           ?? new ImmutableConfigurationSection( new MutableConfigurationSection( $"{contextsSection.Path}:{key}" ) );
            string rootPath = "";
            var r = oContext[RootPathKey];
            if( r != null )
            {
                var p = r is JsonValue v && v.TryGetValue<string>( out var s ) ? NormalizeRelativePath( s ) : null;
                if( p == null )
                {
                    monitor.Error( $"Invalid \"{cSection.Path}:{RootPathKey}\" in '{file}': a relative path in the solution, downward only (no '..', not rooted), empty for the root." );
                    success = false;
                    continue;
                }
                rootPath = p;
            }
            var apps = CkonfReader.ReadApps( monitor, file, CkonfReader.DefaultAppName( name ), oContext, cSection );
            if( apps == null )
            {
                success = false;
                continue;
            }
            var c = new CentralAppContext( name, rootPath, rootPath.Length == 0 ? rootFolder : rootFolder.Combine( rootPath ), cSection, oContext, apps );
            foreach( var a in c.Apps )
            {
                if( appNames.TryGetValue( a.Name, out var other ) )
                {
                    monitor.Error( $"The App '{a.Name}' of the AppContext '{name}' is also an App of the AppContext '{other.Name}' in '{file}': the names of the Apps are unique in the solution (case insensitive)." );
                    success = false;
                }
                else appNames.Add( a.Name, c );
            }
            appContexts.Add( c );
        }
        return success ? new CentralConfiguration( file, section, root, domain, relativeDomain, appDefaults, appContexts, markers ) : null;
    }

    // "Domain": '/' separated segments (no '.': the projects map '/' to '.'). "GlobalDomainPrefix": the domain or one of
    // its prefixes.
    static bool ReadDomain( IActivityMonitor monitor, NormalizedPath file, JsonObject root, out string? domain, out string? relativeDomain )
    {
        domain = null;
        relativeDomain = null;
        var d = root[DomainKey];
        var g = root[GlobalDomainPrefixKey];
        if( d == null )
        {
            if( g == null ) return true;
            monitor.Error( $"\"{GlobalDomainPrefixKey}\" in '{file}' requires a \"{DomainKey}\"." );
            return false;
        }
        if( d is not JsonValue dv || !dv.TryGetValue<string>( out var ds ) || !IsValidDomain( ds ) )
        {
            monitor.Error( $"Invalid \"{DomainKey}\" in '{file}': '/' separated names (letters, digits, '_' and '-'), \"Acme/Europe/Paris\"." );
            return false;
        }
        domain = ds;
        var relative = ds;
        if( g != null )
        {
            if( g is not JsonValue gv
                || !gv.TryGetValue<string>( out var gs )
                || !(ds.Equals( gs, StringComparison.OrdinalIgnoreCase ) || ds.StartsWith( gs + '/', StringComparison.OrdinalIgnoreCase )) )
            {
                monitor.Error( $"Invalid \"{GlobalDomainPrefixKey}\" in '{file}': the \"{DomainKey}\" '{ds}' or one of its prefixes is expected." );
                return false;
            }
            relative = ds.Length == gs.Length ? "" : ds[(gs.Length + 1)..];
        }
        relativeDomain = relative.Length > 0 ? relative.Replace( '/', '.' ) : null;
        return true;

        static bool IsValidDomain( string s ) => s.Split( '/' ).All( p => p.Length > 0 && p.All( c => char.IsAsciiLetterOrDigit( c ) || c == '_' || c == '-' ) );
    }

    static string? NormalizeRelativePath( string path )
    {
        var p = path.Replace( '\\', '/' ).Trim().Trim( '/' );
        if( p.Length == 0 ) return "";
        if( Path.IsPathRooted( path ) || p.Contains( ':' ) ) return null;
        var parts = p.Split( '/' );
        if( parts.Any( s => s.Length == 0 || s == "." || s == ".." ) ) return null;
        return string.Join( '/', parts );
    }

    /// <summary>
    /// Writes the "ckonf.json" of the Builder of an AppContext: its entry (without its "RootPath"), completed by the
    /// contributors (<see cref="IAppBuilderContributor.ProjectFromCentral(IActivityMonitor, CentralProjectionContext)"/>),
    /// then the "Defaults:App" merged in each App (the App wins), and the projects of each App ("AppProject" and
    /// "HostProject", relative to the Builder: the Builder doesn't know the domain of the solution). A root key of the
    /// central configuration that no contributor claims is an error. The file is written only when it changes.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="appContext">The AppContext to project.</param>
    /// <param name="contributors">The contributors, in their order.</param>
    /// <returns>True on success, false on error.</returns>
    public bool WriteProjection( IActivityMonitor monitor, CentralAppContext appContext, IEnumerable<IAppBuilderContributor> contributors )
    {
        Throw.CheckArgument( appContext.Central == this );
        var relative = Path.GetRelativePath( appContext.BuilderFolder, File ).Replace( '\\', '/' );
        return WriteProjection( monitor, appContext, contributors, appContext.BuilderFolder, relative, null );
    }

    // The projection of an AppContext in the folder of a Builder: its own (the development), or the one of a generated
    // Builder (a deployment, see CentralDeployment.WriteBuilderProjection): "adjust" completes each App once it is projected.
    internal bool WriteProjection( IActivityMonitor monitor,
                                   CentralAppContext appContext,
                                   IEnumerable<IAppBuilderContributor> contributors,
                                   NormalizedPath folder,
                                   string source,
                                   Action<CentralApp, JsonObject>? adjust )
    {
        using var _ = monitor.OpenInfo( $"Projecting the AppContext '{appContext.Name}' of '{File}' in '{folder}'." );
        var node = (JsonObject)appContext.Node.DeepClone();
        node.Remove( RootPathKey );
        var apps = new List<CentralProjectedApp>();
        if( node["App"] is JsonObject single )
        {
            apps.Add( new CentralProjectedApp( appContext.Apps[0], single ) );
        }
        else
        {
            var array = (JsonArray)node["Apps"]!;
            for( int i = 0; i < array.Count; ++i ) apps.Add( new CentralProjectedApp( appContext.Apps[i], (JsonObject)array[i]! ) );
        }
        // The projects of the Apps, relative to the Builder (that knows nothing of the domain of the solution).
        foreach( var a in apps )
        {
            a.Node.Remove( "AppProject" );
            a.Node.Remove( "HostProject" );
            a.Node["AppProject"] = Path.GetRelativePath( folder, a.App.AppProjectFolder ).Replace( '\\', '/' );
            a.Node["HostProject"] = Path.GetRelativePath( folder, a.App.HostProjectFolder ).Replace( '\\', '/' );
        }
        var context = new CentralProjectionContext( this, appContext, apps );
        bool success = true;
        var declaringAssemblies = new HashSet<System.Reflection.Assembly>();
        foreach( var c in contributors )
        {
            try
            {
                // The keys that the engine declares ([assembly: CentralConfigurationKey]): claimed for its contributors.
                var assembly = c.GetType().Assembly;
                if( declaringAssemblies.Add( assembly ) )
                {
                    foreach( var k in System.Reflection.CustomAttributeExtensions.GetCustomAttributes<CentralConfigurationKeyAttribute>( assembly ) )
                    {
                        context.CurrentSource = $"the [assembly: CentralConfigurationKey( \"{k.Key}\" )] of '{assembly.GetName().Name}'";
                        context.ClaimKey( k.Key );
                        foreach( var p in k.PerDeployment ?? [] ) context.AddPerDeploymentKey( $"{k.Key}:{p}" );
                    }
                }
                context.CurrentSource = $"the contributor '{c.GetType().ToCSharpName()}'";
                success &= c.ProjectFromCentral( monitor, context );
            }
            catch( Exception ex )
            {
                monitor.Error( $"Contributor '{c.GetType().ToCSharpName()}' failed to project '{File}'.", ex );
                success = false;
            }
        }
        foreach( var (key, _) in _root )
        {
            if( key is AppContextsKey or DomainKey or GlobalDomainPrefixKey or "Defaults" or "$schema" or ConfiguratorKey or CentralDeployment.SettingsKey
                || context.ClaimedKeys.Contains( key ) )
            {
                continue;
            }
            monitor.Error( $"Unknown key '{key}' in '{File}': no contributor handles it (a missing package reference in the builder?)." );
            success = false;
        }
        if( !success ) return false;
        foreach( var k in context.PerDeploymentKeys )
        {
            if( !_perDeploymentKeys.Contains( k ) ) _perDeploymentKeys.Add( k );
        }
        if( _appDefaults != null )
        {
            foreach( var a in apps ) Merge( a.Node, _appDefaults );
        }
        if( adjust != null )
        {
            foreach( var a in apps ) adjust( a.App, a.Node );
        }
        var file = folder.AppendPart( AppContextConfiguration.FileName );
        var text = $"// Generated from {source}: do not edit.{Environment.NewLine}"
                   + node.ToJsonString( _writeOptions )
                   + Environment.NewLine;
        try
        {
            if( !Directory.Exists( folder ) )
            {
                monitor.Error( $"The folder of the Builder of the AppContext '{appContext.Name}' doesn't exist: '{folder}'." );
                return false;
            }
            if( System.IO.File.Exists( file ) && System.IO.File.ReadAllText( file ) == text ) return true;
            System.IO.File.WriteAllText( file, text );
            monitor.Info( $"'{file}' written from '{File}'." );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While writing '{file}'.", ex );
            return false;
        }

        // The defaults are merged: the value of the App wins, the objects are merged. In a "$LOCK", the lowest level wins: a
        // restriction of the defaults can't be raised by an App.
        static void Merge( JsonObject target, JsonObject defaults, bool isLock = false )
        {
            foreach( var (key, value) in defaults )
            {
                var existing = target[key];
                if( existing == null )
                {
                    if( !target.ContainsKey( key ) ) target[key] = value?.DeepClone();
                }
                else if( existing is JsonObject oTarget && value is JsonObject oDefault )
                {
                    Merge( oTarget, oDefault, key.Equals( ConfigurationLockEntry.LockKey, StringComparison.OrdinalIgnoreCase ) );
                }
                else if( isLock && Lower( value, existing ) )
                {
                    target[key] = value?.DeepClone();
                }
            }

            static bool Lower( JsonNode? candidate, JsonNode current )
            {
                return LevelOf( candidate ) is ConfigurationLevel c && LevelOf( current ) is ConfigurationLevel l && c < l;

                static ConfigurationLevel? LevelOf( JsonNode? n )
                    => n is JsonValue v && v.TryGetValue<string>( out var s ) && new ConfigurationLockEntry( "", "", s ).TryGetLevel( out var level )
                        ? level
                        : null;
            }
        }
    }
}

/// <summary>
/// An AppContext of the <see cref="CentralConfiguration"/>: what one engine run builds (its Apps and its AppContext
/// aspects), managed by its Builder, the "{RootPath}/{Name}.Builder" project.
/// </summary>
public sealed class CentralAppContext
{
    internal CentralAppContext( string name,
                                string rootPath,
                                NormalizedPath folder,
                                ImmutableConfigurationSection configuration,
                                JsonObject node,
                                List<(string Name, ImmutableConfigurationSection Section, JsonObject Node)> apps )
    {
        Name = name;
        RootPath = rootPath;
        Folder = folder;
        Configuration = configuration;
        Node = node;
        Apps = apps.Select( a => new CentralApp( this, a.Name, a.Section ) ).ToArray();
    }

    /// <summary>
    /// Gets the central configuration.
    /// </summary>
    public CentralConfiguration Central { get; internal set; } = null!;

    /// <summary>
    /// Gets the name of this AppContext: its key in "AppContexts" (unique in the solution, case insensitive).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the folder of the projects of this AppContext, relative to the <see cref="CentralConfiguration.RootFolder"/>
    /// ('/' separated, empty for the root): its "RootPath".
    /// </summary>
    public string RootPath { get; }

    /// <summary>
    /// Gets the folder of the projects of this AppContext (its Builder and its Apps).
    /// </summary>
    public NormalizedPath Folder { get; }

    /// <summary>
    /// Gets the folder of the Builder: "{RootPath}/{Name}.Builder".
    /// </summary>
    public NormalizedPath BuilderFolder => Folder.AppendPart( $"{Name}.Builder" );

    /// <summary>
    /// Gets the configuration of this AppContext.
    /// </summary>
    public ImmutableConfigurationSection Configuration { get; }

    internal JsonObject Node { get; }

    /// <summary>
    /// Gets the Apps of this AppContext, in the order of the file.
    /// </summary>
    public IReadOnlyList<CentralApp> Apps { get; }

    /// <inheritdoc />
    public override string ToString() => $"AppContext '{Name}'";
}

/// <summary>
/// An App of a <see cref="CentralAppContext"/>.
/// </summary>
public sealed class CentralApp
{
    internal CentralApp( CentralAppContext appContext, string name, ImmutableConfigurationSection configuration )
    {
        AppContext = appContext;
        Name = name;
        Configuration = configuration;
    }

    /// <summary>
    /// Gets the AppContext of this App.
    /// </summary>
    public CentralAppContext AppContext { get; }

    /// <summary>
    /// Gets the name of this App: its party name ("$Jobs"), possibly after the segments of its domain relative to the
    /// domain of the solution ("Customers.$Acme"). Unique in the central configuration, case insensitive.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the name of the projects of this App: the <see cref="CentralConfiguration.RelativeDomain"/>, then its name
    /// without '$' ("Auth3.Jobs", "Auth3.Customers.Acme").
    /// </summary>
    public string ProjectName => AppContext.Central.RelativeDomain is string d
                                    ? $"{d}.{CkonfReader.BareProjectName( Name )}"
                                    : CkonfReader.BareProjectName( Name );

    /// <summary>
    /// Gets the configuration of this App, as written in the central configuration.
    /// </summary>
    public ImmutableConfigurationSection Configuration { get; }

    /// <summary>
    /// Gets the folder of the ".App" project of this App: its "AppProject" (relative to the <see cref="CentralAppContext.Folder"/>),
    /// by default "{ProjectName}.App" in it.
    /// </summary>
    public NormalizedPath AppProjectFolder => ResolveProject( "AppProject", ".App" );

    /// <summary>
    /// Gets the folder of the ".Host" project of this App: its "HostProject" (relative to the <see cref="CentralAppContext.Folder"/>),
    /// by default "{ProjectName}.Host" in it.
    /// </summary>
    public NormalizedPath HostProjectFolder => ResolveProject( "HostProject", ".Host" );

    NormalizedPath ResolveProject( string key, string suffix )
        => System.IO.Path.GetFullPath( System.IO.Path.Combine( AppContext.Folder, Configuration[key] ?? $"{ProjectName}{suffix}" ) );

    /// <inheritdoc />
    public override string ToString() => $"App '{Name}'";
}

/// <summary>
/// An App of the AppContext being projected: its <see cref="Node"/> is the JSON written in the Builder's "ckonf.json",
/// that the contributors complete.
/// </summary>
public sealed class CentralProjectedApp
{
    internal CentralProjectedApp( CentralApp app, JsonObject node )
    {
        App = app;
        Node = node;
    }

    /// <summary>
    /// Gets the App.
    /// </summary>
    public CentralApp App { get; }

    /// <summary>
    /// Gets the JSON of the App in the projection: the contributors complete it.
    /// </summary>
    public JsonObject Node { get; }
}

/// <summary>
/// What the <see cref="IAppBuilderContributor.ProjectFromCentral(IActivityMonitor, CentralProjectionContext)"/> receive:
/// the central configuration and the Apps of the AppContext being projected.
/// </summary>
public sealed class CentralProjectionContext
{
    readonly HashSet<string> _claimedKeys;

    internal CentralProjectionContext( CentralConfiguration central, CentralAppContext appContext, IReadOnlyList<CentralProjectedApp> apps )
    {
        Central = central;
        AppContext = appContext;
        Apps = apps;
        _claimedKeys = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
    }

    /// <summary>
    /// Gets the central configuration.
    /// </summary>
    public CentralConfiguration Central { get; }

    /// <summary>
    /// Gets the AppContext being projected.
    /// </summary>
    public CentralAppContext AppContext { get; }

    /// <summary>
    /// Gets the Apps of the <see cref="AppContext"/>, with their JSON to complete.
    /// </summary>
    public IReadOnlyList<CentralProjectedApp> Apps { get; }

    /// <summary>
    /// Claims a root key of the central configuration: a root key that no contributor claims is an error. A contributor
    /// claims its keys whether they are present or not. The keys declared by the assembly of a contributor
    /// (<see cref="CentralConfigurationKeyAttribute"/>) are claimed before it projects: this is for the keys that only
    /// its code knows.
    /// </summary>
    /// <param name="key">The root key.</param>
    public void ClaimKey( string key )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( key );
        _claimedKeys.Add( key );
    }

    /// <summary>
    /// Gets the claimed root keys.
    /// </summary>
    public IReadOnlySet<string> ClaimedKeys => _claimedKeys;

    readonly List<(string Key, string Source)> _perDeploymentKeys = new List<(string, string)>();

    /// <summary>
    /// Declares a pattern of values per deployment of a key that the contributor reads, relative to the root of the
    /// central configuration (<c>"Links:[*]:Link:Address"</c>): each deployment must write them itself (see
    /// <see cref="CentralDeployment"/>).
    /// </summary>
    /// <param name="pattern">The pattern (':' separated, <c>*</c> for any member, <c>[*]</c> for any item).</param>
    public void AddPerDeploymentKey( string pattern )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( pattern );
        _perDeploymentKeys.Add( (pattern, CurrentSource) );
    }

    internal string CurrentSource { get; set; } = "a contributor";

    internal IReadOnlyList<(string Key, string Source)> PerDeploymentKeys => _perDeploymentKeys;
}
