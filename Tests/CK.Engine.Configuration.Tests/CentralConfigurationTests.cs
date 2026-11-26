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
    // A repository (a ".git" folder) with its central "ckonf.json" and the given folders.
    static NormalizedPath CreateRepository( string central, params string[] folders )
    {
        NormalizedPath root = Path.Combine( Path.GetTempPath(), "CK.AppBuilder.Tests", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( root.AppendPart( ".git" ) );
        File.WriteAllText( root.AppendPart( "ckonf.json" ), central );
        foreach( var b in folders ) Directory.CreateDirectory( root.Combine( b ) );
        return root;
    }

    // What the AppIdentity contributor does: it derives the FullName of the Apps from the domain of the solution.
    sealed class DomainContributor : IAppBuilderContributor
    {
        public bool Contribute( IActivityMonitor monitor, AppBuilderContext context ) => true;

        public bool ProjectFromCentral( IActivityMonitor monitor, CentralProjectionContext context )
        {
            var domain = context.Central.Domain;
            if( domain == null ) return true;
            foreach( var a in context.Apps )
            {
                if( !a.Node.ContainsKey( "FullName" ) ) a.Node.Insert( 0, "FullName", $"{domain}/{a.App.Name}/#Dev" );
            }
            return true;
        }
    }

    const string _sample = """
        {
          // The central configuration.
          "Domain": "Sample/Auth3",
          "GlobalDomainPrefix": "Sample",
          "Defaults": {
            "App": {
              "Logging": { "LogLevel": { "Default": "Warning" } },
              "CK-Monitoring": { "GlobalDefaultFilter": "Debug", "GrandOutput": { "Handlers": { "Console": true } } }
            }
          },
          "AppContexts": {
            "Front": {
              "RootPath": "Front",
              "App": {
                "Local": { "HttpEndpoint": { "Urls": "http://localhost:5300" } },
                "CK-Monitoring": { "GlobalDefaultFilter": "Trace" }
              }
            },
            "Backs": {
              "Aspects": { "SqlServer": { "DefaultDatabaseConnectionString": "Server=.;Database=Db" } },
              "Apps": [ { "Name": "$Back1" }, { "Name": "$Back2", "Logging": false } ]
            }
          }
        }
        """;

    [Test]
    public void the_AppContexts_are_projected_with_the_defaults_of_the_Apps()
    {
        var root = CreateRepository( _sample, "Front/Front.Builder", "Backs.Builder" );
        var frontBuilder = root.Combine( "Front/Front.Builder" );
        CentralConfiguration.TryLoad( TestHelper.Monitor, frontBuilder, out var central ).ShouldBeTrue();
        central.ShouldNotBeNull();
        central.Domain.ShouldBe( "Sample/Auth3" );
        central.RelativeDomain.ShouldBe( "Auth3" );
        central.AppContexts.Select( c => c.Name ).ShouldBe( ["Front", "Backs"] );
        central.AppContexts.Select( c => c.RootPath ).ShouldBe( ["Front", ""] );
        central.AllApps.Select( a => a.Name ).ShouldBe( ["$Front", "$Back1", "$Back2"] );
        central.AllApps.Select( a => a.ProjectName ).ShouldBe( ["Auth3.Front", "Auth3.Back1", "Auth3.Back2"] );

        var front = central.FindAppContextOfBuilder( frontBuilder ).ShouldNotBeNull();
        front.ShouldBeSameAs( central.FindAppContext( "front" ) );
        front.Apps.Single().Name.ShouldBe( "$Front", "The single App is the party of the AppContext's name." );
        front.Apps.Single().AppProjectFolder.ShouldBe( root.Combine( "Front/Auth3.Front.App" ) );
        central.WriteProjection( TestHelper.Monitor, front, [new DomainContributor()] ).ShouldBeTrue();

        var file = frontBuilder.AppendPart( "ckonf.json" );
        var text = File.ReadAllText( file );
        text.ShouldStartWith( "// Generated from ../../ckonf.json: do not edit." );
        var projected = ImmutableConfigurationSection.CreateFromJson( "ckonf", text );
        projected.GetChildren().Select( c => c.Key ).ShouldBe( ["App"], "The RootPath is not projected." );
        projected.GetRequiredSection( "App" ).GetChildren().Select( c => c.Key )
                 .ShouldBe( ["FullName", "Local", "CK-Monitoring", "AppProject", "HostProject", "Logging"] );
        projected["App:FullName"].ShouldBe( "Sample/Auth3/$Front/#Dev" );
        // The projects of the App are relative to the Builder (that doesn't know the domain).
        projected["App:AppProject"].ShouldBe( "../Auth3.Front.App" );
        projected["App:HostProject"].ShouldBe( "../Auth3.Front.Host" );
        // The App wins, the objects are merged.
        projected["App:CK-Monitoring:GlobalDefaultFilter"].ShouldBe( "Trace" );
        projected["App:CK-Monitoring:GrandOutput:Handlers:Console"].ShouldBe( "True" );
        projected["App:Logging:LogLevel:Default"].ShouldBe( "Warning" );

        // The projection is the configuration of the AppContext for its Builder.
        Directory.CreateDirectory( root.Combine( "Front/Auth3.Front.App" ) );
        Directory.CreateDirectory( root.Combine( "Front/Auth3.Front.Host" ) );
        File.WriteAllText( root.AppendPart( "Sample.slnx" ), "<Solution />" );
        var app = AppContextConfiguration.Load( TestHelper.Monitor, frontBuilder, "bin/Debug/net10.0", null, out var engine ).ShouldNotBeNull();
        app.Apps.Single().Name.ShouldBe( "$Front" );
        engine!.BinPaths.Single().Name.ShouldBe( "Auth3.Front", "The BinPath is named after the .App project." );

        // Unchanged: not written again.
        var written = File.GetLastWriteTimeUtc( file );
        System.Threading.Thread.Sleep( 50 );
        central.WriteProjection( TestHelper.Monitor, front, [new DomainContributor()] ).ShouldBeTrue();
        File.GetLastWriteTimeUtc( file ).ShouldBe( written );
    }

    [Test]
    public void a_multi_App_AppContext_keeps_its_aspects_and_an_App_value_wins_over_the_defaults()
    {
        var root = CreateRepository( _sample, "Front/Front.Builder", "Backs.Builder" );
        var central = CentralConfiguration.Parse( TestHelper.Monitor, root.AppendPart( "ckonf.json" ), _sample ).ShouldNotBeNull();
        var backs = central.FindAppContextOfBuilder( root.AppendPart( "Backs.Builder" ) ).ShouldNotBeNull();
        central.WriteProjection( TestHelper.Monitor, backs, [new DomainContributor()] ).ShouldBeTrue();

        var text = File.ReadAllText( root.Combine( "Backs.Builder/ckonf.json" ) );
        text.ShouldStartWith( "// Generated from ../ckonf.json: do not edit." );
        var projected = ImmutableConfigurationSection.CreateFromJson( "ckonf", text );
        projected["Aspects:SqlServer:DefaultDatabaseConnectionString"].ShouldBe( "Server=.;Database=Db" );
        projected["Apps:0:Name"].ShouldBe( "$Back1" );
        projected["Apps:0:FullName"].ShouldBe( "Sample/Auth3/$Back1/#Dev" );
        projected["Apps:0:AppProject"].ShouldBe( "../Auth3.Back1.App" );
        projected["Apps:0:Logging:LogLevel:Default"].ShouldBe( "Warning" );
        projected["Apps:1:Logging"].ShouldBe( "False", "The App's value wins." );
    }

    [Test]
    public void without_domain_the_projects_are_named_after_the_App_and_an_App_of_a_sub_domain_has_its_segments()
    {
        const string json = """
            { "AppContexts": { "Shop": { "RootPath": "Shops", "Apps": [ { "Name": "$Shop" }, { "Name": "Customers.$Acme" } ] } } }
            """;
        var root = CreateRepository( json, "Shops/Shop.Builder" );
        var central = CentralConfiguration.Parse( TestHelper.Monitor, root.AppendPart( "ckonf.json" ), json ).ShouldNotBeNull();
        central.Domain.ShouldBeNull();
        central.AllApps.Select( a => a.ProjectName ).ShouldBe( ["Shop", "Customers.Acme"] );
        central.AllApps.Last().HostProjectFolder.ShouldBe( root.Combine( "Shops/Customers.Acme.Host" ) );
        central.AppContexts[0].BuilderFolder.ShouldBe( root.Combine( "Shops/Shop.Builder" ) );
    }

    [TestCase( "Worker", "$Worker" )]
    [TestCase( "CK.Ng.Admin.Sample", "CK.Ng.Admin.$Sample" )]
    public void the_single_App_is_the_AppContext_name_with_its_party( string appContext, string app )
    {
        var json = $$"""{ "AppContexts": { "{{appContext}}": { "App": {} } } }""";
        var root = CreateRepository( json );
        var central = CentralConfiguration.Parse( TestHelper.Monitor, root.AppendPart( "ckonf.json" ), json ).ShouldNotBeNull();
        central.AllApps.Single().Name.ShouldBe( app );
        central.AllApps.Single().ProjectName.ShouldBe( appContext );
    }

    [Test]
    public void a_root_key_that_no_contributor_claims_is_an_error()
    {
        var json = _sample.Replace( "\"Defaults\"", "\"Links\": [], \"Defaults\"" );
        var root = CreateRepository( json, "Front/Front.Builder", "Backs.Builder" );
        var central = CentralConfiguration.Parse( TestHelper.Monitor, root.AppendPart( "ckonf.json" ), json ).ShouldNotBeNull();
        var front = central.AppContexts[0];
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            central.WriteProjection( TestHelper.Monitor, front, [] ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "Unknown key 'Links'" ) );
        }
        File.Exists( root.Combine( "Front/Front.Builder/ckonf.json" ) ).ShouldBeFalse();
    }

    [Test]
    public void the_configurator_root_key_is_accepted_and_ignored()
    {
        var central = """{ "$configurator": { "World": "https://github.com/CK-Build/CK-Stack", "Version": "1.0.0" },"""
                      + _sample.Substring( _sample.IndexOf( '{' ) + 1 );
        var root = CreateRepository( central, "Front/Front.Builder", "Backs.Builder" );
        var c = CentralConfiguration.Parse( TestHelper.Monitor, root.AppendPart( "ckonf.json" ), central ).ShouldNotBeNull();
        c.WriteProjection( TestHelper.Monitor, c.AppContexts[0], [new DomainContributor()] ).ShouldBeTrue();
        File.ReadAllText( root.Combine( "Front/Front.Builder/ckonf.json" ) ).ShouldNotContain( CentralConfiguration.ConfiguratorKey );
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

    [TestCase( """{ "AppContexts": { "X": { "RootPath": "../Outside", "App": {} } } }""", "downward only" )]
    [TestCase( """{ "AppContexts": { "X": { "RootPath": "A/./B", "App": {} } } }""", "downward only" )]
    [TestCase( """{ "AppContexts": { "X": { "RootPath": "C:/X", "App": {} } } }""", "downward only" )]
    [TestCase( """{ "AppContexts": { "$X": { "App": {} } } }""", "Invalid AppContext name '$X'" )]
    [TestCase( """{ "AppContexts": { "A": { "Apps": [ { "Name": "$X" } ] }, "B": { "Apps": [ { "Name": "$x" } ] } } }""", "the names of the Apps are unique in the solution" )]
    [TestCase( """{ "AppContexts": { "X": { "Apps": [ { "Name": "$A" }, { "Name": "$a" } ] } } }""", "Duplicate App name '$a'" )]
    [TestCase( """{ "AppContexts": { "X": { "Apps": [ { "Name": "A" } ] } } }""", "the name of an App is its party name" )]
    [TestCase( """{ "AppContexts": { "X": { "Apps": [ { "Name": "$A.B" } ] } } }""", "the name of an App is its party name" )]
    [TestCase( """{ "AppContexts": { "X": { } } }""", "must define its App" )]
    [TestCase( """{ "AppContexts": { } }""", "must define \"AppContexts\"" )]
    [TestCase( """{ "Defaults": { "Builder": {} }, "AppContexts": { "X": { "App": {} } } }""", "Invalid \"Defaults:Builder\"" )]
    [TestCase( """{ "Domain": "Sample.Auth3", "AppContexts": { "X": { "App": {} } } }""", "Invalid \"Domain\"" )]
    [TestCase( """{ "Domain": "Sample/Auth3", "GlobalDomainPrefix": "Other", "AppContexts": { "X": { "App": {} } } }""", "Invalid \"GlobalDomainPrefix\"" )]
    [TestCase( """{ "GlobalDomainPrefix": "Sample", "AppContexts": { "X": { "App": {} } } }""", "requires a \"Domain\"" )]
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
