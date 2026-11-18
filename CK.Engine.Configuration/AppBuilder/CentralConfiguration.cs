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
/// It holds its Builders, keyed by the path of their folder ("Front/Front.Builder"), with their aspects and their App or
/// Apps, the defaults of the Apps ("Defaults:App") and what the contributors read ("Domain", "Links"...: the graph of the
/// parties). The "ckonf.json" of each builder is a projection of its entry (<see cref="WriteProjection"/>).
/// <code>
/// {
///   "Domain": "Sample/Auth3",
///   "Defaults": { "App": { "Logging": { ... } } },
///   "Builders": {
///     "Auth3/Auth3.Builder": { "Aspects": { ... }, "App": { "Local": { ... } } },
///     "Backs.Builder": { "Apps": [ { "Name": "Back1", ... }, { "Name": "Back2", ... } ] }
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

    static readonly JsonSerializerOptions _writeOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    readonly JsonObject _root;
    readonly JsonObject? _appDefaults;
    readonly IReadOnlyList<CentralBuilder> _builders;

    CentralConfiguration( NormalizedPath file,
                          ImmutableConfigurationSection configuration,
                          JsonObject root,
                          JsonObject? appDefaults,
                          List<CentralBuilder> builders )
    {
        File = file;
        Configuration = configuration;
        _root = root;
        _appDefaults = appDefaults;
        _builders = builders;
        foreach( var b in builders ) b.Central = this;
    }

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
    /// Gets the builders, in the order of the file.
    /// </summary>
    public IReadOnlyList<CentralBuilder> Builders => _builders;

    /// <summary>
    /// Gets all the Apps of all the builders.
    /// </summary>
    public IEnumerable<CentralApp> AllApps => _builders.SelectMany( b => b.Apps );

    /// <summary>
    /// Finds the builder of a folder.
    /// </summary>
    /// <param name="builderFolder">The builder's folder.</param>
    /// <returns>The builder or null.</returns>
    public CentralBuilder? FindBuilder( NormalizedPath builderFolder )
        => _builders.FirstOrDefault( b => b.Folder.Path.Equals( builderFolder.Path, StringComparison.OrdinalIgnoreCase ) );

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
        // The builders, keyed by the path of their folder.
        if( root["Builders"] is not JsonObject oBuilders || oBuilders.Count == 0 )
        {
            monitor.Error( $"'{file}' must define \"Builders\": {{ \"Path/X.Builder\": {{ ... }} }} (at least one builder, keyed by the path of its folder)." );
            return null;
        }
        var rootFolder = file.RemoveLastPart();
        var builders = new List<CentralBuilder>();
        var appNames = new Dictionary<string, CentralBuilder>( StringComparer.OrdinalIgnoreCase );
        var buildersSection = section.GetRequiredSection( "Builders" );
        foreach( var (key, value) in oBuilders )
        {
            var path = NormalizeBuilderPath( key );
            if( path == null )
            {
                monitor.Error( $"Invalid builder path '{key}' in '{file}': a relative path to the folder of the builder, downward only (no '..', not rooted)." );
                success = false;
                continue;
            }
            if( builders.Any( b => b.Path.Equals( path, StringComparison.OrdinalIgnoreCase ) ) )
            {
                monitor.Error( $"The builder '{path}' is defined twice in '{file}'." );
                success = false;
                continue;
            }
            if( value is not JsonObject oBuilder )
            {
                monitor.Error( $"The builder '{key}' in '{file}' must be an object." );
                success = false;
                continue;
            }
            // The configuration keys can't contain ':' (a separator): the section is found by position.
            var bSection = buildersSection.GetChildren().FirstOrDefault( c => c.Key.Equals( key, StringComparison.OrdinalIgnoreCase ) )
                           ?? new ImmutableConfigurationSection( new MutableConfigurationSection( $"{buildersSection.Path}:{key}" ) );
            var folder = rootFolder.Combine( path );
            var apps = CkonfReader.ReadApps( monitor, file, CkonfReader.DefaultAppName( folder ), oBuilder, bSection );
            if( apps == null )
            {
                success = false;
                continue;
            }
            var b = new CentralBuilder( path, folder, bSection, oBuilder, apps );
            foreach( var a in b.Apps )
            {
                if( appNames.TryGetValue( a.Name, out var other ) )
                {
                    monitor.Error( $"The App '{a.Name}' of the builder '{path}' is also an App of the builder '{other.Path}' in '{file}': the names of the Apps are unique in the solution (case insensitive)." );
                    success = false;
                }
                else appNames.Add( a.Name, b );
            }
            builders.Add( b );
        }
        return success ? new CentralConfiguration( file, section, root, appDefaults, builders ) : null;
    }

    static string? NormalizeBuilderPath( string key )
    {
        var p = key.Replace( '\\', '/' ).Trim().Trim( '/' );
        if( p.Length == 0 || Path.IsPathRooted( key ) || p.Contains( ':' ) ) return null;
        var parts = p.Split( '/' );
        if( parts.Any( s => s.Length == 0 || s == "." || s == ".." ) ) return null;
        return string.Join( '/', parts );
    }

    /// <summary>
    /// Writes the "ckonf.json" of a builder: its entry, completed by the contributors
    /// (<see cref="IAppBuilderContributor.ProjectFromCentral(IActivityMonitor, CentralProjectionContext)"/>), then the
    /// "Defaults:App" merged in each App (the App wins). A root key of the central configuration that no contributor
    /// claims is an error. The file is written only when it changes.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="builder">The builder to project.</param>
    /// <param name="contributors">The contributors, in their order.</param>
    /// <returns>True on success, false on error.</returns>
    public bool WriteProjection( IActivityMonitor monitor, CentralBuilder builder, IEnumerable<IAppBuilderContributor> contributors )
    {
        Throw.CheckArgument( builder.Central == this );
        using var _ = monitor.OpenInfo( $"Projecting the builder '{builder.Path}' of '{File}'." );
        var node = (JsonObject)builder.Node.DeepClone();
        var apps = new List<CentralProjectedApp>();
        if( node["App"] is JsonObject single )
        {
            apps.Add( new CentralProjectedApp( builder.Apps[0], single ) );
        }
        else
        {
            var array = (JsonArray)node["Apps"]!;
            for( int i = 0; i < array.Count; ++i ) apps.Add( new CentralProjectedApp( builder.Apps[i], (JsonObject)array[i]! ) );
        }
        var context = new CentralProjectionContext( this, builder, apps );
        bool success = true;
        foreach( var c in contributors )
        {
            try
            {
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
            if( key is "Builders" or "Defaults" or "$schema" || context.ClaimedKeys.Contains( key ) ) continue;
            monitor.Error( $"Unknown key '{key}' in '{File}': no contributor handles it (a missing package reference in the builder?)." );
            success = false;
        }
        if( !success ) return false;
        if( _appDefaults != null )
        {
            foreach( var a in apps ) Merge( a.Node, _appDefaults );
        }
        var file = builder.Folder.AppendPart( AppBuilderConfiguration.FileName );
        var relative = string.Concat( Enumerable.Repeat( "../", builder.Path.Count( c => c == '/' ) + 1 ) ) + FileName;
        var text = $"// Generated from {relative}: do not edit.{Environment.NewLine}"
                   + node.ToJsonString( _writeOptions )
                   + Environment.NewLine;
        try
        {
            if( !Directory.Exists( builder.Folder ) )
            {
                monitor.Error( $"The folder of the builder '{builder.Path}' doesn't exist: '{builder.Folder}'." );
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

        // The defaults are merged: the value of the App wins, the objects are merged.
        static void Merge( JsonObject target, JsonObject defaults )
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
                    Merge( oTarget, oDefault );
                }
            }
        }
    }
}

/// <summary>
/// A builder of the <see cref="CentralConfiguration"/>.
/// </summary>
public sealed class CentralBuilder
{
    internal CentralBuilder( string path,
                             NormalizedPath folder,
                             ImmutableConfigurationSection configuration,
                             JsonObject node,
                             List<(string Name, ImmutableConfigurationSection Section, JsonObject Node)> apps )
    {
        Path = path;
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
    /// Gets the path of the builder's folder, relative to the <see cref="CentralConfiguration.RootFolder"/> ('/' separated):
    /// its key in "Builders".
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the builder's folder.
    /// </summary>
    public NormalizedPath Folder { get; }

    /// <summary>
    /// Gets the configuration of this builder.
    /// </summary>
    public ImmutableConfigurationSection Configuration { get; }

    internal JsonObject Node { get; }

    /// <summary>
    /// Gets the Apps of this builder, in the order of the file.
    /// </summary>
    public IReadOnlyList<CentralApp> Apps { get; }

    /// <inheritdoc />
    public override string ToString() => $"Builder '{Path}'";
}

/// <summary>
/// An App of a <see cref="CentralBuilder"/>.
/// </summary>
public sealed class CentralApp
{
    internal CentralApp( CentralBuilder builder, string name, ImmutableConfigurationSection configuration )
    {
        Builder = builder;
        Name = name;
        Configuration = configuration;
    }

    /// <summary>
    /// Gets the builder of this App.
    /// </summary>
    public CentralBuilder Builder { get; }

    /// <summary>
    /// Gets the name of this App (unique in the central configuration, case insensitive).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the configuration of this App, as written in the central configuration.
    /// </summary>
    public ImmutableConfigurationSection Configuration { get; }

    /// <inheritdoc />
    public override string ToString() => $"App '{Name}'";
}

/// <summary>
/// An App of the builder being projected: its <see cref="Node"/> is the JSON written in the builder's "ckonf.json",
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
/// the central configuration and the Apps of the builder being projected.
/// </summary>
public sealed class CentralProjectionContext
{
    readonly HashSet<string> _claimedKeys;

    internal CentralProjectionContext( CentralConfiguration central, CentralBuilder builder, IReadOnlyList<CentralProjectedApp> apps )
    {
        Central = central;
        Builder = builder;
        Apps = apps;
        _claimedKeys = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
    }

    /// <summary>
    /// Gets the central configuration.
    /// </summary>
    public CentralConfiguration Central { get; }

    /// <summary>
    /// Gets the builder being projected.
    /// </summary>
    public CentralBuilder Builder { get; }

    /// <summary>
    /// Gets the Apps of the <see cref="Builder"/>, with their JSON to complete.
    /// </summary>
    public IReadOnlyList<CentralProjectedApp> Apps { get; }

    /// <summary>
    /// Claims a root key of the central configuration: a root key that no contributor claims is an error. A contributor
    /// claims its keys whether they are present or not.
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
}
