using CK.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace CK.Setup;

/// <summary>
/// The configuration of a builder, read from the "ckonf.json" of its "XXX.Builder" project: its Apps keyed by their name,
/// and its Builder aspects (the ones that apply to the whole engine run). It is the future root of the engine: the engine
/// still receives an <see cref="EngineConfiguration"/>, that this projects (a BinPath per App, the root of an optional
/// "CKSetup.xml" for the aspects that are not deduced yet).
/// <para>
/// The paths are taken in control, top-down: everything is relative to the "ckonf.json" folder; an App "X" is the
/// "../X.App" project (its bin folder is the one of the builder: "bin/{Configuration}/{TargetFramework}") and the
/// "../X.Host" project ("AppProject" and "HostProject" depart from this convention). A path that leaves the solution's
/// folder is an error.
/// </para>
/// <para>
/// The "appsettings.json" of each host is a projection of its App: its content without its compile time keys, the keys
/// that the contributors project in a section (<see cref="AppConfiguration.ProjectInSection(string, string)"/>) moved
/// in it.
/// </para>
/// </summary>
public sealed class AppBuilderConfiguration
{
    /// <summary>
    /// The configuration file name, in the "XXX.Builder" project.
    /// </summary>
    public const string FileName = "ckonf.json";

    // The compile time keys of an App (not projected).
    static readonly string[] _appKeys = ["AppProject", "HostProject", "Types", "ExcludedTypes", "Assemblies",
                                         "DiscoverAssembliesFromPath", "CompileOption", "GenerateSourceFiles", "Aspects"];

    static readonly JsonSerializerOptions _writeOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    readonly NormalizedPath _builderFolder;
    readonly JsonObject _appsNode;
    readonly bool _projectCompileKeys;
    readonly IReadOnlyList<AppConfiguration> _apps;
    readonly AppAspects _aspects;

    AppBuilderConfiguration( NormalizedPath builderFolder,
                             JsonObject appsNode,
                             bool projectCompileKeys,
                             IReadOnlyList<AppConfiguration> apps,
                             AppAspects aspects )
    {
        _builderFolder = builderFolder;
        _appsNode = appsNode;
        _projectCompileKeys = projectCompileKeys;
        _apps = apps;
        _aspects = aspects;
    }

    /// <summary>
    /// Gets the Apps, sorted by name.
    /// </summary>
    public IReadOnlyList<AppConfiguration> Apps => _apps;

    /// <summary>
    /// Gets the Builder aspects (the "Aspects" at the root).
    /// </summary>
    public AppAspects Aspects => _aspects;

    /// <summary>
    /// Reads the "ckonf.json" of a builder and creates the engine configuration.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="builderFolder">The "XXX.Builder" project folder (that contains the "ckonf.json").</param>
    /// <param name="msBuildOutputPath">The output path of the builder ("bin/Debug/net10.0"): the one of the ".App" projects.</param>
    /// <param name="ckSetup">The optional "CKSetup.xml" root: its aspects and its global options. It must not define BinPaths.</param>
    /// <param name="engineConfiguration">The engine configuration on success.</param>
    /// <returns>The builder configuration, or null on error (errors are logged).</returns>
    public static AppBuilderConfiguration? Load( IActivityMonitor monitor,
                                                 NormalizedPath builderFolder,
                                                 NormalizedPath msBuildOutputPath,
                                                 XElement? ckSetup,
                                                 out EngineConfiguration? engineConfiguration )
    {
        engineConfiguration = null;
        var file = builderFolder.AppendPart( FileName );
        using var _ = monitor.OpenInfo( $"Reading '{file}'." );
        string json;
        JsonObject root;
        ImmutableConfigurationSection section;
        try
        {
            json = File.ReadAllText( file );
            // The configuration is case insensitive: "Back1" and "back1" are the same key.
            using( var doc = JsonDocument.Parse( json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true } ) )
            {
                if( !CheckDuplicateKeys( monitor, file, doc.RootElement, "" ) ) return null;
            }
            section = ImmutableConfigurationSection.CreateFromJson( "ckonf", json );
            root = JsonNode.Parse( json,
                                   new JsonNodeOptions { PropertyNameCaseInsensitive = true },
                                   new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true } )
                   as JsonObject
                   ?? throw new InvalidDataException( "A JSON object is expected." );
        }
        catch( Exception ex )
        {
            monitor.Error( $"Invalid '{file}'.", ex );
            return null;
        }
        if( !FindSolutionFolder( monitor, builderFolder, out var solutionFolder ) ) return null;

        bool success = true;
        foreach( var c in section.GetChildren() )
        {
            if( c.Key is not ("Apps" or "Aspects" or "ProjectCompileKeys" or "$schema") )
            {
                monitor.Error( $"Unknown key '{c.Key}' in '{file}': the root has Apps, Aspects and ProjectCompileKeys." );
                success = false;
            }
        }
        var appsSection = section.TryGetSection( "Apps" );
        if( appsSection == null || appsSection.GetChildren().Count == 0 )
        {
            monitor.Error( $"'{file}' must define \"Apps\": {{ \"Name\": {{ ... }} }} (at least one App, keyed by its name)." );
            return null;
        }
        bool projectCompileKeys = false;
        var p = section["ProjectCompileKeys"];
        if( p != null && !bool.TryParse( p, out projectCompileKeys ) )
        {
            monitor.Error( $"Invalid \"ProjectCompileKeys\": '{p}' (true or false is expected)." );
            success = false;
        }
        var appsNode = root["Apps"] as JsonObject;
        if( appsNode == null )
        {
            monitor.Error( $"\"Apps\" in '{file}' must be an object: the Apps are keyed by their name." );
            return null;
        }
        var aspectsSection = section.TryGetSection( "Aspects" );
        if( section["Aspects"] != null )
        {
            monitor.Error( $"\"Aspects\" in '{file}' must be an object: the aspects are keyed by their name." );
            success = false;
        }
        if( ckSetup != null && ckSetup.Elements( EngineConfiguration.xBinPaths ).Elements().Any() )
        {
            monitor.Error( $"The \"CKSetup.xml\" of '{builderFolder}' defines BinPaths: with a '{FileName}', the BinPaths are its \"Apps\" (the \"CKSetup.xml\" may only define the aspects that are not deduced yet)." );
            success = false;
        }
        if( !success ) return null;

        // The Apps, sorted by name (their order means nothing: the engine's FirstBinPath is the first name).
        var apps = new List<(string Name, NormalizedPath App, NormalizedPath Host, ImmutableConfigurationSection Section)>();
        var xBinPaths = new XElement( EngineConfiguration.xBinPaths );
        foreach( var a in appsSection.GetChildren().OrderBy( a => a.Key, StringComparer.OrdinalIgnoreCase ) )
        {
            if( a.Value != null )
            {
                monitor.Error( $"\"{a.Path}\" must be an object." );
                success = false;
                continue;
            }
            if( a["Aspects"] != null )
            {
                monitor.Error( $"\"{a.Path}:Aspects\" must be an object: the aspects are keyed by their name." );
                success = false;
                continue;
            }
            var appProject = ResolveProject( monitor, builderFolder, solutionFolder, a, "AppProject", $"{a.Key}.App" );
            var hostProject = ResolveProject( monitor, builderFolder, solutionFolder, a, "HostProject", $"{a.Key}.Host" );
            if( appProject.IsEmptyPath || hostProject.IsEmptyPath )
            {
                success = false;
                continue;
            }
            var x = CreateBinPathXml( monitor, a, appProject.Combine( msBuildOutputPath ), hostProject );
            if( x == null )
            {
                success = false;
                continue;
            }
            xBinPaths.Add( x );
            apps.Add( (a.Key, appProject, hostProject, a) );
        }
        if( !success ) return null;

        var xRoot = ckSetup != null ? new XElement( ckSetup ) : new XElement( "Setup" );
        xRoot.Elements( EngineConfiguration.xBinPaths ).Remove();
        xRoot.Add( xBinPaths );
        try
        {
            engineConfiguration = new EngineConfiguration( xRoot );
            if( engineConfiguration.BasePath.IsEmptyPath ) engineConfiguration.BasePath = builderFolder;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While creating the engine configuration from '{file}'.", ex );
            return null;
        }
        var e = engineConfiguration;
        var appConfigurations = apps.Select( a => new AppConfiguration( a.Name, a.App, a.Host, a.Section, e.FindRequiredBinPath( a.Name ) ) )
                                    .ToArray();
        return new AppBuilderConfiguration( builderFolder, appsNode, projectCompileKeys, appConfigurations, new AppAspects( aspectsSection ) );
    }

    /// <summary>
    /// Checks that every aspect (of the builder and of each App) has been handled by a contributor.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public bool CheckAspectsHandled( IActivityMonitor monitor )
    {
        bool success = true;
        foreach( var a in _aspects.UnhandledAspects.Concat( _apps.SelectMany( a => a.Aspects.UnhandledAspects ) ) )
        {
            monitor.Error( $"No package handles the aspect \"{a.Path}\" (a missing package reference in the builder?)." );
            success = false;
        }
        return success;
    }

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

    static bool FindSolutionFolder( IActivityMonitor monitor, NormalizedPath builderFolder, out NormalizedPath solutionFolder )
    {
        for( var f = builderFolder.RemoveLastPart(); !f.IsEmptyPath; f = f.RemoveLastPart() )
        {
            if( Directory.Exists( f ) && Directory.EnumerateFiles( f, "*.sln*" ).Any( s => s.EndsWith( ".sln", StringComparison.OrdinalIgnoreCase )
                                                                                       || s.EndsWith( ".slnx", StringComparison.OrdinalIgnoreCase ) ) )
            {
                solutionFolder = f;
                return true;
            }
        }
        monitor.Error( $"No solution file (.sln or .slnx) found above '{builderFolder}': the paths of '{FileName}' must stay in the solution's folder." );
        solutionFolder = default;
        return false;
    }

    static NormalizedPath ResolveProject( IActivityMonitor monitor,
                                          NormalizedPath builderFolder,
                                          NormalizedPath solutionFolder,
                                          ImmutableConfigurationSection app,
                                          string key,
                                          string conventionName )
    {
        var relative = app[key] ?? $"../{conventionName}";
        NormalizedPath full = Path.GetFullPath( Path.Combine( builderFolder, relative ) );
        if( !full.StartsWith( solutionFolder, strict: true ) )
        {
            monitor.Error( $"The path '{relative}' of \"{app.Path}:{key}\" ('{full}') leaves the solution's folder '{solutionFolder}'." );
            return default;
        }
        if( !Directory.Exists( full ) )
        {
            monitor.Error( $"The project folder '{full}' of the App '{app.Key}' doesn't exist{(app[key] == null ? $" (by convention: '{conventionName}'; \"{key}\" departs from it)" : "")}." );
            return default;
        }
        return full;
    }

    // The engine's BinPath: the same XML as a "CKSetup.xml" BinPath, so that the engine receives what it always received.
    static XElement? CreateBinPathXml( IActivityMonitor monitor, ImmutableConfigurationSection a, NormalizedPath binFolder, NormalizedPath hostProject )
    {
        var x = new XElement( EngineConfiguration.xBinPath,
                              new XAttribute( EngineConfiguration.xName, a.Key ),
                              new XAttribute( EngineConfiguration.xPath, binFolder ),
                              new XElement( EngineConfiguration.xProjectPath, hostProject.Path ) );
        if( a["DiscoverAssembliesFromPath"] is string discover ) x.Add( new XAttribute( EngineConfiguration.xDiscoverAssembliesFromPath, discover ) );
        if( a["CompileOption"] is string compile ) x.Add( new XElement( EngineConfiguration.xCompileOption, compile ) );
        if( a["GenerateSourceFiles"] is string generate ) x.Add( new XElement( EngineConfiguration.xGenerateSourceFiles, generate ) );
        var assemblies = ReadList( a, "Assemblies" );
        if( assemblies.Count > 0 ) x.Add( new XElement( EngineConfiguration.xAssemblies, assemblies.Select( a => new XElement( EngineConfiguration.xAssembly, a ) ) ) );
        var excluded = ReadList( a, "ExcludedTypes" );
        if( excluded.Count > 0 ) x.Add( new XElement( EngineConfiguration.xExcludedTypes, excluded.Select( t => new XElement( EngineConfiguration.xType, t ) ) ) );
        var types = a.TryGetSection( "Types" );
        if( types != null )
        {
            var xTypes = new XElement( EngineConfiguration.xTypes );
            foreach( var t in types.GetChildren() )
            {
                // A type is its assembly qualified name, or { "Name": "...", "Kind": "..." }.
                var name = t.Value ?? t["Name"];
                if( string.IsNullOrWhiteSpace( name ) )
                {
                    monitor.Error( $"Invalid \"{t.Path}\": a type is its assembly qualified name, or {{ \"Name\": \"...\", \"Kind\": \"...\" }}." );
                    return null;
                }
                var kind = t.Value == null ? t["Kind"] : null;
                xTypes.Add( new XElement( EngineConfiguration.xType, kind != null ? new XAttribute( EngineConfiguration.xKind, kind ) : null, name ) );
            }
            x.Add( xTypes );
        }
        return x;
    }

    static List<string> ReadList( ImmutableConfigurationSection c, string key )
    {
        var value = c[key];
        return value != null
                ? value.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).ToList()
                : c.TryGetSection( key )?.GetChildren()
                                         .Select( v => v.Value )
                                         .Where( v => !string.IsNullOrWhiteSpace( v ) )
                                         .Select( v => v!.Trim() )
                                         .ToList()
                  ?? [];
    }

    /// <summary>
    /// Writes the "appsettings.json" of each host: the content of its App without its compile time keys (the ones of
    /// the App, and the ones the contributors declared unless "ProjectCompileKeys" is true), the keys projected in a
    /// section moved in it. A file is written only when it changes.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public bool WriteProjections( IActivityMonitor monitor )
    {
        bool success = true;
        foreach( var a in _apps )
        {
            var node = (JsonObject)_appsNode.First( kv => StringComparer.OrdinalIgnoreCase.Equals( kv.Key, a.Name ) ).Value!.DeepClone();
            foreach( var k in _appKeys ) node.Remove( k );
            if( !_projectCompileKeys )
            {
                foreach( var path in a.CompileKeys ) Remove( node, path.Split( ':' ) );
                // A declaration that the removal emptied stays: it is written as true.
                foreach( var path in a.DeclarationKeys ) KeepDeclaration( node, path.Split( ':' ) );
            }
            if( !MoveInSections( monitor, a, node ) || !SetValues( monitor, a, node ) )
            {
                success = false;
                continue;
            }
            var file = a.HostProjectPath.AppendPart( "appsettings.json" );
            var text = $"// Generated from {_builderFolder.LastPart}/{FileName}: do not edit.{Environment.NewLine}"
                       + node.ToJsonString( _writeOptions )
                       + Environment.NewLine;
            try
            {
                if( File.Exists( file ) && File.ReadAllText( file ) == text ) continue;
                File.WriteAllText( file, text );
                monitor.Info( $"'{file}' written from '{FileName}'." );
            }
            catch( Exception ex )
            {
                monitor.Error( $"While writing '{file}'.", ex );
                success = false;
            }
        }
        return success;

        static void Remove( JsonObject o, string[] path )
        {
            for( int i = 0; i < path.Length - 1; ++i )
            {
                if( o[path[i]] is not JsonObject child ) return;
                o = child;
            }
            o.Remove( path[^1] );
        }

        static void KeepDeclaration( JsonObject o, string[] path )
        {
            for( int i = 0; i < path.Length - 1; ++i )
            {
                if( o[path[i]] is not JsonObject child ) return;
                o = child;
            }
            if( o[path[^1]] is JsonObject { Count: 0 } ) o[path[^1]] = true;
        }

        // The values projected by the aspects: their root section can't be a key of the App.
        static bool SetValues( IActivityMonitor monitor, AppConfiguration a, JsonObject node )
        {
            bool success = true;
            foreach( var (path, value) in a.ProjectedValues.OrderBy( kv => kv.Key, StringComparer.OrdinalIgnoreCase ) )
            {
                var parts = path.Split( ':' );
                if( a.Configuration.TryGetSection( parts[0] ) != null )
                {
                    monitor.Error( $"The key '{parts[0]}' of {a} is the root section in which '{path}' is projected: it can't be a key of the App." );
                    success = false;
                    continue;
                }
                var o = node;
                for( int i = 0; i < parts.Length - 1; ++i )
                {
                    if( o[parts[i]] is not JsonObject child )
                    {
                        child = new JsonObject();
                        o[parts[i]] = child;
                    }
                    o = child;
                }
                o[parts[^1]] = value;
            }
            return success;
        }

        // The keys projected in a section are moved in it, in their order in the App. The section can't be a key of the
        // App itself.
        static bool MoveInSections( IActivityMonitor monitor, AppConfiguration a, JsonObject node )
        {
            bool success = true;
            foreach( var sectionName in a.ProjectedSections.Values.Distinct( StringComparer.OrdinalIgnoreCase ) )
            {
                if( node.ContainsKey( sectionName ) )
                {
                    monitor.Error( $"The key '{sectionName}' of {a} is the section in which keys are projected: it can't be a key of the App." );
                    success = false;
                }
            }
            if( !success ) return false;
            var moved = node.Where( kv => a.ProjectedSections.ContainsKey( kv.Key ) ).Select( kv => kv.Key ).ToList();
            var sections = new Dictionary<string, JsonObject>( StringComparer.OrdinalIgnoreCase );
            foreach( var key in moved )
            {
                var sectionName = a.ProjectedSections[key];
                var value = node[key];
                node.Remove( key );
                if( !sections.TryGetValue( sectionName, out var s ) )
                {
                    s = new JsonObject();
                    sections.Add( sectionName, s );
                }
                s[key] = value;
            }
            // The sections come first: the identity of the App before its other sections.
            var rest = node.ToList();
            node.Clear();
            foreach( var (name, s) in sections ) node[name] = s;
            foreach( var (key, value) in rest ) node[key] = value;
            return true;
        }
    }
}
