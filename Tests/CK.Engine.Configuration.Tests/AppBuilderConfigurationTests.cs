using CK.Core;
using CK.Setup;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppBuilder.Tests;

[TestFixture]
public class AppBuilderConfigurationTests
{
    // A solution folder with a "Backs.Builder" project and the .App/.Host projects of the given Apps.
    static NormalizedPath CreateSolution( string ckonf, params string[] apps )
    {
        NormalizedPath root = Path.Combine( Path.GetTempPath(), "CK.AppBuilder.Tests", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( root );
        File.WriteAllText( root.AppendPart( "Sample.slnx" ), "<Solution />" );
        var builder = root.AppendPart( "Backs.Builder" );
        Directory.CreateDirectory( builder );
        File.WriteAllText( builder.AppendPart( "ckonf.json" ), ckonf );
        foreach( var a in apps )
        {
            Directory.CreateDirectory( root.AppendPart( $"{a}.App" ) );
            Directory.CreateDirectory( root.AppendPart( $"{a}.Host" ) );
        }
        return builder;
    }

    static AppBuilderConfiguration? Load( NormalizedPath builder, out EngineConfiguration? engine, XElement? ckSetup = null )
        => AppBuilderConfiguration.Load( TestHelper.Monitor, builder, "bin/Debug/net10.0", ckSetup, out engine );

    [Test]
    public void the_Apps_are_keyed_by_name_and_their_projects_follow_the_convention()
    {
        var builder = CreateSolution( """
            {
              // Comments are allowed.
              "Apps": {
                "Back2": {
                  "CompileOption": "Parse",
                  "DiscoverAssembliesFromPath": true,
                  "Types": [ "CK.Core.IActivityMonitor, CK.ActivityMonitor",
                             { "Name": "CK.Core.IParallelLogger, CK.ActivityMonitor", "Kind": "IsContainerConfiguredService|IsScoped" } ],
                  "FullName": "Sample/$Back2"
                },
                "Back1": { "FullName": "Sample/$Back1" }
              }
            }
            """, "Back1", "Back2" );
        var root = builder.RemoveLastPart();
        var app = Load( builder, out var engine ).ShouldNotBeNull();
        engine.ShouldNotBeNull();

        // Sorted by name: their order in the file means nothing. An App is a BinPath of the engine.
        app.Apps.Select( a => a.Name ).ShouldBe( ["Back1", "Back2"] );
        engine.BinPaths.Select( b => b.Name ).ShouldBe( ["Back1", "Back2"] );
        engine.FirstBinPath.Name.ShouldBe( "Back1" );

        var back2 = app.Apps[1];
        back2.AppProjectPath.ShouldBe( root.AppendPart( "Back2.App" ) );
        back2.HostProjectPath.ShouldBe( root.AppendPart( "Back2.Host" ) );
        back2.BinPathConfiguration.Path.ShouldBe( root.AppendPart( "Back2.App" ).Combine( "bin/Debug/net10.0" ) );
        back2.BinPathConfiguration.ProjectPath.ShouldBe( root.AppendPart( "Back2.Host" ) );
        back2.BinPathConfiguration.CompileOption.ShouldBe( CompileOption.Parse );
        back2.BinPathConfiguration.DiscoverAssembliesFromPath.ShouldBeTrue();
        back2.BinPathConfiguration.Types.Count.ShouldBe( 2 );
        back2.BinPathConfiguration.Types.Single( t => t.Type == typeof( IParallelLogger ) ).Kind
             .ShouldBe( ConfigurableAutoServiceKind.IsContainerConfiguredService | ConfigurableAutoServiceKind.IsScoped );
        back2.Configuration["FullName"].ShouldBe( "Sample/$Back2" );
    }

    [Test]
    public void the_hosts_appsettings_are_projections_without_the_compile_keys()
    {
        var builder = CreateSolution( """
            {
              "Apps": {
                "Front": {
                  "Types": [ "CK.Core.IActivityMonitor, CK.ActivityMonitor" ],
                  "Aspects": { "Globalization": { "ActiveCultures": "fr" } },
                  "FullName": "Sample/$Front",
                  "Local": { "PartyClient": { "App": { "Type": "TypeScript", "Kind": "DefaultApp", "StaticFiles": { "AssetsMaxAge": "01:00:00" } } } },
                  "ConnectionStrings": { "Db": "Server=.;Database=Sample;Trust='yes'" }
                }
              }
            }
            """, "Front" );
        var app = Load( builder, out _ ).ShouldNotBeNull();
        var front = app.Apps.Single();
        // A contributor declares the compile time keys of the sections it handles.
        front.AddCompileKey( "Local:PartyClient:App:Type" );
        front.AddCompileKey( "Local:PartyClient:App:Kind" );
        app.WriteProjections( TestHelper.Monitor ).ShouldBeTrue();

        var file = front.HostProjectPath.AppendPart( "appsettings.json" );
        var text = File.ReadAllText( file );
        text.ShouldStartWith( "// Generated from Backs.Builder/ckonf.json: do not edit." );
        text.ShouldNotContain( "Types" );
        text.ShouldNotContain( "\"Type\"" );
        text.ShouldNotContain( "DefaultApp" );
        // The aspects are compile time.
        text.ShouldNotContain( "Aspects" );
        text.ShouldNotContain( "ActiveCultures" );
        text.ShouldContain( "\"AssetsMaxAge\": \"01:00:00\"" );
        // No escaping of the quotes in the values.
        text.ShouldContain( "Trust='yes'" );
        var projected = ImmutableConfigurationSection.CreateFromJson( "appsettings", text );
        projected["FullName"].ShouldBe( "Sample/$Front" );

        // Unchanged: not written again.
        var written = File.GetLastWriteTimeUtc( file );
        System.Threading.Thread.Sleep( 50 );
        app.WriteProjections( TestHelper.Monitor ).ShouldBeTrue();
        File.GetLastWriteTimeUtc( file ).ShouldBe( written );
    }

    [Test]
    public void the_keys_projected_in_a_section_are_moved_in_it()
    {
        var builder = CreateSolution( """
            {
              "Apps": {
                "Back": {
                  "Logging": { "LogLevel": { "Default": "Warning" } },
                  "FullName": "Sample/$Back",
                  "Local": { "HttpEndpoint": true },
                  "Parties": [ { "PartyName": "$Front" } ]
                }
              }
            }
            """, "Back" );
        var app = Load( builder, out _ ).ShouldNotBeNull();
        var back = app.Apps.Single();
        // What the AppIdentity contributor does: the keys of the party are in "CK-AppIdentity" in the host.
        foreach( var k in new[] { "FullName", "Local", "Parties", "Defaults" } ) back.ProjectInSection( k, "CK-AppIdentity" );
        app.WriteProjections( TestHelper.Monitor ).ShouldBeTrue();

        var projected = ImmutableConfigurationSection.CreateFromJson( "appsettings", File.ReadAllText( back.HostProjectPath.AppendPart( "appsettings.json" ) ) );
        projected.GetChildren().Select( c => c.Key ).ShouldBe( ["CK-AppIdentity", "Logging"] );
        projected["CK-AppIdentity:FullName"].ShouldBe( "Sample/$Back" );
        projected["CK-AppIdentity:Local:HttpEndpoint"].ShouldBe( "True" );
        projected["CK-AppIdentity:Parties:0:PartyName"].ShouldBe( "$Front" );
        projected["Logging:LogLevel:Default"].ShouldBe( "Warning" );
    }

    [Test]
    public void a_declaration_emptied_by_the_projection_is_written_true()
    {
        var builder = CreateSolution( """
            {
              "Apps": {
                "Back": {
                  "Local": { "PartyClient": { "App": { "NgApp": { "Barrels": [ "a" ] }, "Hosting": { "DevServerPort": 4200, "UseDist": true }, "Other": { "Port": 1 } } } }
                }
              }
            }
            """, "Back" );
        var app = Load( builder, out _ ).ShouldNotBeNull();
        var back = app.Apps.Single();
        back.AddCompileKey( "Local:PartyClient:App:NgApp:Barrels" );
        back.AddCompileKey( "Local:PartyClient:App:Hosting:DevServerPort" );
        back.AddCompileKey( "Local:PartyClient:App:Other:Port" );
        back.AddDeclarationKey( "Local:PartyClient:App:NgApp" );
        back.AddDeclarationKey( "Local:PartyClient:App:Hosting" );
        app.WriteProjections( TestHelper.Monitor ).ShouldBeTrue();

        var projected = ImmutableConfigurationSection.CreateFromJson( "appsettings", File.ReadAllText( back.HostProjectPath.AppendPart( "appsettings.json" ) ) );
        projected["Local:PartyClient:App:NgApp"].ShouldBe( "True", "Emptied: the declaration stays." );
        projected["Local:PartyClient:App:Hosting:UseDist"].ShouldBe( "True", "Not emptied: the section stays." );
        projected["Local:PartyClient:App:Other"].ShouldBeNull( "Not a declaration: an emptied section disappears." );
    }

    [Test]
    public void the_values_projected_by_an_aspect_are_root_sections_of_the_host()
    {
        var builder = CreateSolution( """{ "Apps": { "Back": { "FullName": "Sample/$Back", "Logging": { "LogLevel": { "Default": "Warning" } } } } }""", "Back" );
        var app = Load( builder, out _ ).ShouldNotBeNull();
        var back = app.Apps.Single();
        back.ProjectInSection( "FullName", "CK-AppIdentity" );
        // What the "SqlServer" aspect does.
        back.ProjectValue( "SqlServer:DefaultDatabaseConnectionString", "Server=.;Database=Db" );
        app.WriteProjections( TestHelper.Monitor ).ShouldBeTrue();

        var projected = ImmutableConfigurationSection.CreateFromJson( "appsettings", File.ReadAllText( back.HostProjectPath.AppendPart( "appsettings.json" ) ) );
        projected.GetChildren().Select( c => c.Key ).ShouldBe( ["CK-AppIdentity", "Logging", "SqlServer"] );
        projected["SqlServer:DefaultDatabaseConnectionString"].ShouldBe( "Server=.;Database=Db" );
    }

    [Test]
    public void a_projected_value_cannot_be_in_a_key_of_the_App()
    {
        var builder = CreateSolution( """{ "Apps": { "Back": { "SqlServer": {} } } }""", "Back" );
        var app = Load( builder, out _ ).ShouldNotBeNull();
        app.Apps[0].ProjectValue( "SqlServer:DefaultDatabaseConnectionString", "Server=.;Database=Db" );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            app.WriteProjections( TestHelper.Monitor ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "is the root section in which 'SqlServer:DefaultDatabaseConnectionString' is projected" ) );
        }
    }

    [Test]
    public void a_section_in_which_keys_are_projected_cannot_be_a_key_of_the_App()
    {
        var builder = CreateSolution( """{ "Apps": { "Back": { "FullName": "Sample/$Back", "CK-AppIdentity": {} } } }""", "Back" );
        var app = Load( builder, out _ ).ShouldNotBeNull();
        app.Apps[0].ProjectInSection( "FullName", "CK-AppIdentity" );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            app.WriteProjections( TestHelper.Monitor ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "is the section in which keys are projected" ) );
        }
    }

    [Test]
    public void an_aspect_that_no_contributor_handles_is_an_error()
    {
        var builder = CreateSolution( """
            {
              "Aspects": { "SqlServer": { "DefaultDatabaseConnectionString": "..." } },
              "Apps": { "Back": { "Aspects": { "Globalization": { "ActiveCultures": "fr" }, "Unknown": {} } } }
            }
            """, "Back" );
        var app = Load( builder, out _ ).ShouldNotBeNull();
        // A contributor reads the "Globalization" aspect.
        app.Apps[0].Aspects.TryGetAspect( "globalization" ).ShouldNotBeNull()["ActiveCultures"].ShouldBe( "fr" );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            app.CheckAspectsHandled( TestHelper.Monitor ).ShouldBeFalse();
            texts.Count( t => t.Contains( "No package handles the aspect" ) ).ShouldBe( 2 );
            texts.ShouldContain( t => t.Contains( "\"ckonf:Aspects:SqlServer\"" ) );
            texts.ShouldContain( t => t.Contains( "\"ckonf:Apps:Back:Aspects:Unknown\"" ) );
        }
        app.Aspects.TryGetAspect( "SqlServer" ).ShouldNotBeNull();
        app.Apps[0].Aspects.TryGetAspect( "Unknown" ).ShouldNotBeNull();
        app.CheckAspectsHandled( TestHelper.Monitor ).ShouldBeTrue();
    }

    [Test]
    public void ProjectCompileKeys_keeps_the_compile_keys_of_the_leaves()
    {
        var builder = CreateSolution( """
            {
              "ProjectCompileKeys": true,
              "Apps": { "Front": { "Types": [], "Local": { "Kind": "DefaultApp" } } }
            }
            """, "Front" );
        var app = Load( builder, out _ ).ShouldNotBeNull();
        app.Apps[0].AddCompileKey( "Local:Kind" );
        app.WriteProjections( TestHelper.Monitor ).ShouldBeTrue();
        var text = File.ReadAllText( app.Apps[0].HostProjectPath.AppendPart( "appsettings.json" ) );
        text.ShouldContain( "DefaultApp" );
        // The keys of the App itself are never projected.
        text.ShouldNotContain( "Types" );
    }

    [Test]
    public void AppProject_and_HostProject_depart_from_the_convention()
    {
        var builder = CreateSolution( """
            { "Apps": { "Back": { "AppProject": "../Other/Back.App", "HostProject": "../Back.Host" } } }
            """, "Back" );
        var root = builder.RemoveLastPart();
        Directory.CreateDirectory( root.Combine( "Other/Back.App" ) );
        var app = Load( builder, out _ ).ShouldNotBeNull();
        app.Apps[0].AppProjectPath.ShouldBe( root.Combine( "Other/Back.App" ) );
    }

    [TestCase( """{ "Apps": { "Back": { "HostProject": "../../Outside.Host" } } }""", "leaves the solution's folder" )]
    [TestCase( """{ "Apps": { "Missing": {} } }""", "doesn't exist (by convention: 'Missing.App'" )]
    [TestCase( """{ "Apps": { "Back": {}, "back": {} } }""", "Duplicate key 'Apps:back'" )]
    [TestCase( """{ "Apps": [ { "Back": {} } ] }""", "must be an object" )]
    [TestCase( """{ "Apps": {} }""", "at least one App" )]
    [TestCase( """{ "Other": 1, "Apps": { "Back": {} } }""", "Unknown key 'Other'" )]
    [TestCase( """{ "App": { "BinPaths": { "Back": {} } } }""", "Unknown key 'App'" )]
    [TestCase( """{ "ProjectCompileKeys": "maybe", "Apps": { "Back": {} } }""", "Invalid \"ProjectCompileKeys\"" )]
    [TestCase( """{ "Aspects": "SqlServer", "Apps": { "Back": {} } }""", "\"Aspects\" in" )]
    [TestCase( """{ "Apps": { "Back": { "Aspects": "Globalization" } } }""", "\"ckonf:Apps:Back:Aspects\" must be an object" )]
    public void invalid_configurations_are_errors( string ckonf, string expected )
    {
        var builder = CreateSolution( ckonf, "Back" );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            Load( builder, out var engine ).ShouldBeNull();
            engine.ShouldBeNull();
            texts.ShouldContain( t => t.Contains( expected ), $"Expected: {expected}" );
        }
    }

    [Test]
    public void the_CKSetup_xml_brings_the_root_but_no_BinPath()
    {
        var builder = CreateSolution( """{ "Apps": { "Back": {} } }""", "Back" );
        var withRoot = XElement.Parse( "<Setup><TraceDependencySorterInput>true</TraceDependencySorterInput></Setup>" );
        Load( builder, out var engine, withRoot ).ShouldNotBeNull();
        engine.ShouldNotBeNull().TraceDependencySorterInput.ShouldBeTrue();

        var withBinPaths = XElement.Parse( "<Setup><BinPaths><BinPath Path=\"../Back.App/bin/Debug/net10.0\" /></BinPaths></Setup>" );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            Load( builder, out _, withBinPaths ).ShouldBeNull();
            texts.ShouldContain( t => t.Contains( "defines BinPaths" ) );
        }
    }
}
