using CK.Core;
using CK.Setup;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppBuilder.Tests;

[TestFixture]
public class CentralConfigurationTests
{
    // A repository (a ".git" folder) with its central "ckonf.json" and the given builder folders.
    static NormalizedPath CreateRepository( string central, params string[] builders )
    {
        NormalizedPath root = Path.Combine( Path.GetTempPath(), "CK.AppBuilder.Tests", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( root.AppendPart( ".git" ) );
        File.WriteAllText( root.AppendPart( "ckonf.json" ), central );
        foreach( var b in builders ) Directory.CreateDirectory( root.Combine( b ) );
        return root;
    }

    // What the AppIdentity contributor does: it claims "Domain" and derives the FullName.
    sealed class DomainContributor : IAppBuilderContributor
    {
        public bool Contribute( IActivityMonitor monitor, AppBuilderContext context ) => true;

        public bool ProjectFromCentral( IActivityMonitor monitor, CentralProjectionContext context )
        {
            context.ClaimKey( "Domain" );
            var domain = context.Central.Configuration["Domain"];
            if( domain == null ) return true;
            foreach( var a in context.Apps )
            {
                if( !a.Node.ContainsKey( "FullName" ) ) a.Node.Insert( 0, "FullName", $"{domain}/${a.App.Name}/#Dev" );
            }
            return true;
        }
    }

    const string _sample = """
        {
          // The central configuration.
          "Domain": "Sample/Auth3",
          "Defaults": {
            "App": {
              "Logging": { "LogLevel": { "Default": "Warning" } },
              "CK-Monitoring": { "GlobalDefaultFilter": "Debug", "GrandOutput": { "Handlers": { "Console": true } } }
            }
          },
          "Builders": {
            "Front/Front.Builder": {
              "App": {
                "Local": { "HttpEndpoint": { "Urls": "http://localhost:5300" } },
                "CK-Monitoring": { "GlobalDefaultFilter": "Trace" }
              }
            },
            "Backs.Builder": {
              "Aspects": { "SqlServer": { "DefaultDatabaseConnectionString": "Server=.;Database=Db" } },
              "Apps": [ { "Name": "Back1" }, { "Name": "Back2", "Logging": false } ]
            }
          }
        }
        """;

    [Test]
    public void the_builders_are_projected_with_the_defaults_of_the_Apps()
    {
        var root = CreateRepository( _sample, "Front/Front.Builder", "Backs.Builder" );
        var frontBuilder = root.Combine( "Front/Front.Builder" );
        CentralConfiguration.TryLoad( TestHelper.Monitor, frontBuilder, out var central ).ShouldBeTrue();
        central.ShouldNotBeNull();
        central.Builders.Select( b => b.Path ).ShouldBe( ["Front/Front.Builder", "Backs.Builder"] );
        central.AllApps.Select( a => a.Name ).ShouldBe( ["Front", "Back1", "Back2"] );

        var front = central.FindBuilder( frontBuilder ).ShouldNotBeNull();
        front.Apps.Single().Name.ShouldBe( "Front", "Named after the builder's folder." );
        central.WriteProjection( TestHelper.Monitor, front, [new DomainContributor()] ).ShouldBeTrue();

        var file = frontBuilder.AppendPart( "ckonf.json" );
        var text = File.ReadAllText( file );
        text.ShouldStartWith( "// Generated from ../../ckonf.json: do not edit." );
        var projected = ImmutableConfigurationSection.CreateFromJson( "ckonf", text );
        projected.GetChildren().Select( c => c.Key ).ShouldBe( ["App"] );
        projected.GetRequiredSection( "App" ).GetChildren().Select( c => c.Key ).ShouldBe( ["FullName", "Local", "CK-Monitoring", "Logging"] );
        projected["App:FullName"].ShouldBe( "Sample/Auth3/$Front/#Dev" );
        // The App wins, the objects are merged.
        projected["App:CK-Monitoring:GlobalDefaultFilter"].ShouldBe( "Trace" );
        projected["App:CK-Monitoring:GrandOutput:Handlers:Console"].ShouldBe( "True" );
        projected["App:Logging:LogLevel:Default"].ShouldBe( "Warning" );

        // The projection is a builder configuration.
        Directory.CreateDirectory( root.Combine( "Front/Front.App" ) );
        Directory.CreateDirectory( root.Combine( "Front/Front.Host" ) );
        File.WriteAllText( root.AppendPart( "Sample.slnx" ), "<Solution />" );
        var app = AppBuilderConfiguration.Load( TestHelper.Monitor, frontBuilder, "bin/Debug/net10.0", null, out _ ).ShouldNotBeNull();
        app.Apps.Single().Name.ShouldBe( "Front" );

        // Unchanged: not written again.
        var written = File.GetLastWriteTimeUtc( file );
        System.Threading.Thread.Sleep( 50 );
        central.WriteProjection( TestHelper.Monitor, front, [new DomainContributor()] ).ShouldBeTrue();
        File.GetLastWriteTimeUtc( file ).ShouldBe( written );
    }

    [Test]
    public void a_multi_App_builder_keeps_its_aspects_and_an_App_value_wins_over_the_defaults()
    {
        var root = CreateRepository( _sample, "Front/Front.Builder", "Backs.Builder" );
        var central = CentralConfiguration.Parse( TestHelper.Monitor, root.AppendPart( "ckonf.json" ), _sample ).ShouldNotBeNull();
        var backs = central.FindBuilder( root.AppendPart( "Backs.Builder" ) ).ShouldNotBeNull();
        central.WriteProjection( TestHelper.Monitor, backs, [new DomainContributor()] ).ShouldBeTrue();

        var text = File.ReadAllText( root.Combine( "Backs.Builder/ckonf.json" ) );
        text.ShouldStartWith( "// Generated from ../ckonf.json: do not edit." );
        var projected = ImmutableConfigurationSection.CreateFromJson( "ckonf", text );
        projected["Aspects:SqlServer:DefaultDatabaseConnectionString"].ShouldBe( "Server=.;Database=Db" );
        projected["Apps:0:Name"].ShouldBe( "Back1" );
        projected["Apps:0:FullName"].ShouldBe( "Sample/Auth3/$Back1/#Dev" );
        projected["Apps:0:Logging:LogLevel:Default"].ShouldBe( "Warning" );
        projected["Apps:1:Logging"].ShouldBe( "False", "The App's value wins." );
    }

    [Test]
    public void a_root_key_that_no_contributor_claims_is_an_error()
    {
        var root = CreateRepository( _sample, "Front/Front.Builder", "Backs.Builder" );
        var central = CentralConfiguration.Parse( TestHelper.Monitor, root.AppendPart( "ckonf.json" ), _sample ).ShouldNotBeNull();
        var front = central.Builders[0];
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            central.WriteProjection( TestHelper.Monitor, front, [] ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "Unknown key 'Domain'" ) );
        }
        File.Exists( root.Combine( "Front/Front.Builder/ckonf.json" ) ).ShouldBeFalse();
    }

    [Test]
    public void without_central_configuration_nothing_happens()
    {
        NormalizedPath root = Path.Combine( Path.GetTempPath(), "CK.AppBuilder.Tests", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( root.AppendPart( ".git" ) );
        Directory.CreateDirectory( root.AppendPart( "X.Builder" ) );
        CentralConfiguration.TryLoad( TestHelper.Monitor, root.AppendPart( "X.Builder" ), out var central ).ShouldBeTrue();
        central.ShouldBeNull();
    }

    [TestCase( """{ "Builders": { "../Outside.Builder": { "App": {} } } }""", "downward only" )]
    [TestCase( """{ "Builders": { "A/./X.Builder": { "App": {} } } }""", "downward only" )]
    [TestCase( """{ "Builders": { "C:/X.Builder": { "App": {} } } }""", "downward only" )]
    [TestCase( """{ "Builders": { "X.Builder": { "App": {} }, "X.Builder/": { "App": {} } } }""", "is defined twice" )]
    [TestCase( """{ "Builders": { "A/X.Builder": { "App": {} }, "B/X.Builder": { "App": {} } } }""", "the names of the Apps are unique in the solution" )]
    [TestCase( """{ "Builders": { "X.Builder": { "Apps": [ { "Name": "A" }, { "Name": "a" } ] } } }""", "Duplicate App name 'a'" )]
    [TestCase( """{ "Builders": { "X.Builder": { } } }""", "must define its App" )]
    [TestCase( """{ "Builders": { } }""", "must define \"Builders\"" )]
    [TestCase( """{ "Defaults": { "Builder": {} }, "Builders": { "X.Builder": { "App": {} } } }""", "Invalid \"Defaults:Builder\"" )]
    public void invalid_central_configurations_are_errors( string json, string expected )
    {
        NormalizedPath root = Path.Combine( Path.GetTempPath(), "CK.AppBuilder.Tests", Guid.NewGuid().ToString( "N" ) );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            CentralConfiguration.Parse( TestHelper.Monitor, root.AppendPart( "ckonf.json" ), json ).ShouldBeNull();
            texts.ShouldContain( t => t.Contains( expected ), $"Expected: {expected}" );
        }
    }
}
