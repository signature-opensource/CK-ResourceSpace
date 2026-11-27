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
/// The configuration of an AppContext, read from the "ckonf.json" of its Builder (the "{Name}.Builder" project): its App
/// ("App", by default the AppContext name with a '$' before its last segment: "$Worker", "CK.Ng.Admin.$Sample") or its Apps ("Apps", an array of Apps named after their party:
/// "$Jobs"), and its AppContext aspects (the ones that apply to the whole engine run). It is the future root of the
/// engine: the engine still receives an <see cref="EngineConfiguration"/>, that this projects (a BinPath per App, named
/// after its ".App" project, the root of an optional "CKSetup.xml" for the aspects that are not deduced yet).
/// <para>
/// The paths are taken in control, top-down: everything is relative to the "ckonf.json" folder; an App "$X" is the
/// "../X.App" project (its bin folder is the one of the Builder: "bin/{Configuration}/{TargetFramework}") and the
/// "../X.Host" project ("AppProject" and "HostProject" depart from this convention: the projection of the central
/// configuration always writes them). A path that leaves the solution's folder is an error.
/// </para>
/// <para>
/// The "appsettings.json" of each host is a projection of its App: its content without its compile time keys, the keys
/// that the contributors project in a section (<see cref="AppConfiguration.ProjectInSection(string, string)"/>) moved
/// in it. Its "architecture.md" describes the App from its content, with its compile time choices
/// (<see cref="AppConfiguration.AddArchitecture(string, string)"/>).
/// </para>
/// </summary>
public sealed partial class AppContextConfiguration
{
    /// <summary>
    /// The configuration file name, in the "XXX.Builder" project.
    /// </summary>
    public const string FileName = "ckonf.json";

    /// <summary>
    /// The file written next to the "appsettings.json" of each host: the App described from its content, with its compile
    /// time choices (see <see cref="AppConfiguration.AddArchitecture(string, string)"/>).
    /// </summary>
    public const string ArchitectureFileName = "architecture.md";

    // The "architecture.md" of an App: the App itself (its projects, its type set, its configurable types by package),
    // then the sections of the contributors, in their order.
    string GetArchitecture( AppConfiguration a, JsonObject appNode )
    {
        var nl = Environment.NewLine;
        var b = new System.Text.StringBuilder();
        b.Append( "<!-- Generated from " ).Append( _builderFolder.LastPart ).Append( '/' ).Append( FileName ).Append( ": do not edit. -->" ).Append( nl )
         .Append( "# The App '" ).Append( a.Name ).Append( '\'' ).Append( nl ).Append( nl )
         .Append( "Its run time configuration is `appsettings.json`; this document describes it from its content and its compile time choices." ).Append( nl ).Append( nl );
        b.Append( "## The projects" ).Append( nl ).Append( nl )
         .Append( "- The App: `" ).Append( Path.GetRelativePath( a.HostProjectPath, a.AppProjectPath ).Replace( '\\', '/' ) ).Append( '`' ).Append( nl )
         .Append( "- The Builder: `" ).Append( Path.GetRelativePath( a.HostProjectPath, _builderFolder ).Replace( '\\', '/' ) ).Append( '`' ).Append( nl ).Append( nl );
        b.Append( "## The type set" ).Append( nl ).Append( nl );
        bool any = false;
        foreach( var key in new[] { "Types", "ExcludedTypes", "Assemblies" } )
        {
            if( appNode[key] is not JsonNode n ) continue;
            any = true;
            var items = n is JsonArray array ? array.Select( i => i?.ToString() ?? "" ).ToList() : [n.ToString()];
            b.Append( "- " ).Append( key ).Append( ": " )
             .Append( items.Count == 0 ? "*(none)*" : string.Join( ", ", items.Select( i => $"`{i}`" ) ) ).Append( nl );
        }
        if( !any ) b.Append( "The types of the packages of the App project (discovered from its folder)." ).Append( nl );
        b.Append( nl );
        if( a.ConfigurableTypes.Count > 0 )
        {
            b.Append( "## The configurable types by package" ).Append( nl ).Append( nl )
             .Append( "| Package | Configurable types |" ).Append( nl ).Append( "|---|---|" ).Append( nl );
            foreach( var g in a.ConfigurableTypes.GroupBy( t => t.Assembly.GetName().Name ).OrderBy( g => g.Key, StringComparer.Ordinal ) )
            {
                var names = g.Select( t => t.GetCustomAttributes( inherit: false ).OfType<IConfigurableTypeAttribute>().FirstOrDefault()?.Name ?? t.Name )
                             .Distinct( StringComparer.OrdinalIgnoreCase )
                             .Order( StringComparer.OrdinalIgnoreCase );
                b.Append( "| " ).Append( g.Key ).Append( " | " ).Append( string.Join( ", ", names ) ).Append( " |" ).Append( nl );
            }
            b.Append( nl );
        }
        foreach( var (title, markdown) in a.Architecture )
        {
            b.Append( "## " ).Append( title ).Append( nl ).Append( nl ).Append( markdown.ReplaceLineEndings( nl ).TrimEnd() ).Append( nl ).Append( nl );
        }
        return b.ToString().TrimEnd() + nl;
    }

    // The compile time keys of an App (not projected).
    static readonly string[] _appKeys = ["Name", "AppProject", "HostProject", "Types", "ExcludedTypes", "Assemblies",
                                         "DiscoverAssembliesFromPath", "CompileOption", "GenerateSourceFiles", "Aspects"];

    static readonly JsonSerializerOptions _writeOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    readonly NormalizedPath _builderFolder;
    readonly IReadOnlyList<JsonObject> _appNodes;
    readonly IReadOnlyList<AppConfiguration> _apps;
    readonly AppAspects _aspects;
    readonly IReadOnlyList<ConfigurationLockEntry> _builderLocks;

    AppContextConfiguration( NormalizedPath builderFolder,
                             IReadOnlyList<JsonObject> appNodes,
                             IReadOnlyList<AppConfiguration> apps,
                             AppAspects aspects,
                             IReadOnlyList<ConfigurationLockEntry> builderLocks )
    {
        _builderFolder = builderFolder;
        _appNodes = appNodes;
        _apps = apps;
        _aspects = aspects;
        _builderLocks = builderLocks;
    }

    /// <summary>
    /// Gets the Apps, sorted by name.
    /// </summary>
    public IReadOnlyList<AppConfiguration> Apps => _apps;

    /// <summary>
    /// Gets the AppContext aspects (the "Aspects" at the root).
    /// </summary>
    public AppAspects Aspects => _aspects;

    /// <summary>
    /// Reads the "ckonf.json" of a Builder and creates the engine configuration.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="builderFolder">The "XXX.Builder" project folder (that contains the "ckonf.json").</param>
    /// <param name="msBuildOutputPath">The output path of the builder ("bin/Debug/net10.0"): the one of the ".App" projects.</param>
    /// <param name="ckSetup">The optional "CKSetup.xml" root: its aspects and its global options. It must not define BinPaths.</param>
    /// <param name="engineConfiguration">The engine configuration on success.</param>
    /// <returns>The builder configuration, or null on error (errors are logged).</returns>
    public static AppContextConfiguration? Load( IActivityMonitor monitor,
                                                 NormalizedPath builderFolder,
                                                 NormalizedPath msBuildOutputPath,
                                                 XElement? ckSetup,
                                                 out EngineConfiguration? engineConfiguration )
    {
        engineConfiguration = null;
        var file = builderFolder.AppendPart( FileName );
        using var _ = monitor.OpenInfo( $"Reading '{file}'." );
        if( !CkonfReader.TryRead( monitor, file, out var root, out var section ) ) return null;
        if( !FindSolutionFolder( monitor, builderFolder, out var solutionFolder ) ) return null;
        // The "$LOCK" are a build input: they are removed before anything reads the configuration.
        var locks = new List<ConfigurationLockEntry>();
        if( !ConfigurationLockEntry.Extract( monitor, root, section.Path, locks ) ) return null;
        if( locks.Count > 0 ) section = ImmutableConfigurationSection.CreateFromJson( section.Path, root.ToJsonString() );

        bool success = true;
        foreach( var c in section.GetChildren() )
        {
            if( c.Key is not ("App" or "Apps" or "Aspects" or "$schema") )
            {
                monitor.Error( $"Unknown key '{c.Key}' in '{file}': the root has App (or Apps) and Aspects." );
                success = false;
            }
        }
        var aspectsSection = section.TryGetSection( "Aspects" );
        if( section["Aspects"] != null )
        {
            monitor.Error( $"\"Aspects\" in '{file}' must be an object: the aspects are keyed by their name." );
            success = false;
        }
        var declaredApps = CkonfReader.ReadApps( monitor, file, CkonfReader.DefaultAppName( CkonfReader.AppContextNameOf( builderFolder ) ), root, section );
        if( declaredApps == null ) return null;
        if( ckSetup != null && ckSetup.Elements( EngineConfiguration.xBinPaths ).Elements().Any() )
        {
            monitor.Error( $"The \"CKSetup.xml\" of '{builderFolder}' defines BinPaths: with a '{FileName}', the BinPaths are its \"Apps\" (the \"CKSetup.xml\" may only define the aspects that are not deduced yet)." );
            success = false;
        }
        if( !success ) return null;

        // The Apps, sorted by name (their order means nothing: the engine's FirstBinPath is the first name).
        var apps = new List<(string Name, string BinPathName, NormalizedPath App, NormalizedPath Host, ImmutableConfigurationSection Section, JsonObject Node)>();
        var xBinPaths = new XElement( EngineConfiguration.xBinPaths );
        foreach( var (name, a, node) in declaredApps.OrderBy( a => a.Name, StringComparer.OrdinalIgnoreCase ) )
        {
            if( a["Aspects"] != null )
            {
                monitor.Error( $"\"{a.Path}:Aspects\" must be an object: the aspects are keyed by their name." );
                success = false;
                continue;
            }
            var bare = CkonfReader.BareProjectName( name );
            var appProject = ResolveProject( monitor, builderFolder, solutionFolder, name, a, "AppProject", $"{bare}.App" );
            var hostProject = ResolveProject( monitor, builderFolder, solutionFolder, name, a, "HostProject", $"{bare}.Host" );
            if( appProject.IsEmptyPath || hostProject.IsEmptyPath )
            {
                success = false;
                continue;
            }
            // The BinPath is named after the ".App" project ("Auth3.Jobs"): an App's name has a '$'.
            var binPathName = appProject.LastPart.EndsWith( ".App", StringComparison.OrdinalIgnoreCase ) ? appProject.LastPart[..^4] : appProject.LastPart;
            var x = CreateBinPathXml( monitor, binPathName, a, appProject.Combine( msBuildOutputPath ), hostProject );
            if( x == null )
            {
                success = false;
                continue;
            }
            xBinPaths.Add( x );
            apps.Add( (name, binPathName, appProject, hostProject, a, node) );
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
        var appConfigurations = apps.Select( a => new AppConfiguration( a.Name, a.App, a.Host, a.Section, e.FindRequiredBinPath( a.BinPathName ) ) )
                                    .ToArray();
        // The "$LOCK" of an App are its own; the ones of the AppContext aspects lock what they project in the hosts.
        var builderLocks = new List<ConfigurationLockEntry>();
        var builderAspects = $"{section.Path}:Aspects";
        foreach( var l in locks )
        {
            var app = appConfigurations.FirstOrDefault( a => IsUnder( l.ObjectPath, a.Configuration.Path ) );
            if( app != null ) app.AddLock( l );
            else if( IsUnder( l.ObjectPath, builderAspects ) && !l.ObjectPath.Equals( builderAspects, StringComparison.OrdinalIgnoreCase ) )
            {
                builderLocks.Add( l );
            }
            else
            {
                monitor.Error( $"{l} is outside the Apps and the AppContext aspects: a \"$LOCK\" restricts the values of an App or of an AppContext aspect." );
                success = false;
            }
        }
        if( !success ) return null;
        return new AppContextConfiguration( builderFolder, apps.Select( a => a.Node ).ToArray(), appConfigurations, new AppAspects( aspectsSection ), builderLocks );

        static bool IsUnder( string path, string prefix ) => path.Equals( prefix, StringComparison.OrdinalIgnoreCase )
                                                              || path.StartsWith( prefix + ":", StringComparison.OrdinalIgnoreCase );
    }

    /// <summary>
    /// Checks that every aspect key (of the AppContext and of each App) names an aspect that the Builder applied.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public bool CheckAspectsHandled( IActivityMonitor monitor )
    {
        bool success = true;
        foreach( var a in _aspects.UnhandledAspects.Concat( _apps.SelectMany( a => a.Aspects.UnhandledAspects ) ) )
        {
            monitor.Error( $"No package handles the aspect \"{a.Path}\" (a missing package reference in the Builder?)." );
            success = false;
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
                                          string appName,
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
            monitor.Error( $"The project folder '{full}' of the App '{appName}' doesn't exist{(app[key] == null ? $" (by convention: '{conventionName}'; \"{key}\" departs from it)" : "")}." );
            return default;
        }
        return full;
    }

    // The engine's BinPath: the same XML as a "CKSetup.xml" BinPath, so that the engine receives what it always received.
    static XElement? CreateBinPathXml( IActivityMonitor monitor, string name, ImmutableConfigurationSection a, NormalizedPath binFolder, NormalizedPath hostProject )
    {
        var x = new XElement( EngineConfiguration.xBinPath,
                              new XAttribute( EngineConfiguration.xName, name ),
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
                var typeName = t.Value ?? t["Name"];
                if( string.IsNullOrWhiteSpace( typeName ) )
                {
                    monitor.Error( $"Invalid \"{t.Path}\": a type is its assembly qualified name, or {{ \"Name\": \"...\", \"Kind\": \"...\" }}." );
                    return null;
                }
                var kind = t.Value == null ? t["Kind"] : null;
                xTypes.Add( new XElement( EngineConfiguration.xType, kind != null ? new XAttribute( EngineConfiguration.xKind, kind ) : null, typeName ) );
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
    /// the App, and the ones the contributors declared), the keys projected in a section moved in it; and its
    /// "architecture.md": the App described from its content, with its compile time choices (see
    /// <see cref="AppConfiguration.AddArchitecture(string, string)"/>). A file is written only when it changes.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    public bool WriteProjections( IActivityMonitor monitor )
    {
        bool success = true;
        for( int i = 0; i < _apps.Count; ++i )
        {
            var a = _apps[i];
            var node = (JsonObject)_appNodes[i].DeepClone();
            foreach( var k in _appKeys ) node.Remove( k );
            foreach( var path in a.CompileKeys ) Remove( node, path.Split( ':' ) );
            // A declaration that the removal emptied stays: it is written as true.
            foreach( var path in a.DeclarationKeys ) KeepDeclaration( node, path.Split( ':' ) );
            // The burnt values are in the generated code, not in the file.
            foreach( var path in a.BurntKeys ) Remove( node, path.Split( ':' ) );
            if( !MoveInSections( monitor, a, node ) || !SetValues( monitor, a, node ) )
            {
                success = false;
                continue;
            }
            var text = $"// Generated from {_builderFolder.LastPart}/{FileName}: do not edit.{Environment.NewLine}"
                       + node.ToJsonString( _writeOptions )
                       + Environment.NewLine;
            success &= Write( monitor, a.HostProjectPath.AppendPart( "appsettings.json" ), text );
            success &= Write( monitor, a.HostProjectPath.AppendPart( ArchitectureFileName ), GetArchitecture( a, _appNodes[i] ) );
        }
        return success;

        static bool Write( IActivityMonitor monitor, NormalizedPath file, string text )
        {
            try
            {
                if( File.Exists( file ) && File.ReadAllText( file ) == text ) return true;
                File.WriteAllText( file, text );
                monitor.Info( $"'{file}' written from '{FileName}'." );
                return true;
            }
            catch( Exception ex )
            {
                monitor.Error( $"While writing '{file}'.", ex );
                return false;
            }
        }

        // A value in an array is kept: removing it would shift the indices of the next ones.
        static void Remove( JsonObject o, string[] path )
        {
            JsonNode? n = o;
            for( int i = 0; i < path.Length - 1; ++i )
            {
                n = n switch
                {
                    JsonObject obj => obj[path[i]],
                    JsonArray a when int.TryParse( path[i], out var idx ) && idx >= 0 && idx < a.Count => a[idx],
                    _ => null
                };
                if( n == null ) return;
            }
            if( n is JsonObject parent ) parent.Remove( path[^1] );
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
                // A burnt value is in the generated code, not in the file.
                if( a.BinPathConfiguration.CompiledValues.ContainsKey( path ) ) continue;
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
