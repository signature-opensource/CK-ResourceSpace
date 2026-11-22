using CK.Core;
using CK.Setup;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppBuilder.Tests;

[TestFixture]
public class CentralDeploymentTests
{
    const string _central = """
        {
          "Domain": "Sample/Auth3",
          "Defaults": { "App": { "Logging": { "LogLevel": { "Default": "Warning" } } } },
          "Builders": {
            "Front/Front.Builder": {
              "App": {
                "Local": { "HttpEndpoint": { "Urls": "http://localhost:5300" } },
                "Demo": { "Password": "pass" },
                "$PER_DEPLOYMENT": [ "Demo" ]
              }
            }
          },
          "Links": [ { "From": "$Front", "To": "$Auth3", "Link": { "Address": "tcp:127.0.0.1:37300", "AutoTrustKey": "Once" } } ],
          "$PER_DEPLOYMENT": [ "Links:[*]:Link:AutoTrustKey" ]
        }
        """;

    // A solution: its central "ckonf.json" and the "ckonf.json" of its deployments (by their path in "Deployment").
    static CentralConfiguration Create( string central, params (string Path, string Overlay)[] deployments )
    {
        NormalizedPath root = Path.Combine( Path.GetTempPath(), "CK.AppBuilder.Tests", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( root );
        var file = root.AppendPart( CentralConfiguration.FileName );
        File.WriteAllText( file, central );
        foreach( var (path, overlay) in deployments )
        {
            var folder = root.AppendPart( CentralDeployment.FolderName ).Combine( path );
            Directory.CreateDirectory( folder );
            if( overlay != null ) File.WriteAllText( folder.AppendPart( CentralConfiguration.FileName ), overlay );
        }
        return CentralConfiguration.Parse( TestHelper.Monitor, file, central ).ShouldNotBeNull();
    }

    static IReadOnlyList<CentralDeployment> Load( CentralConfiguration central )
    {
        CentralDeployment.TryLoad( TestHelper.Monitor, central, out var deployments ).ShouldBeTrue();
        return deployments;
    }

    static void ShouldFail( CentralConfiguration central, string message )
    {
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            CentralDeployment.TryLoad( TestHelper.Monitor, central, out _ ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( message ) );
        }
    }

    [Test]
    public void the_markers_are_removed_from_the_central_configuration()
    {
        var central = Create( _central );
        central.PerDeploymentMarkers.Select( m => (m.ObjectPath, m.Pattern) )
               .ShouldBe( [("Builders:Front/Front.Builder:App", "Demo"), ("", "Links:[*]:Link:AutoTrustKey")], ignoreOrder: true );
        central.Configuration.AsEnumerable().ShouldNotContain( kv => kv.Key.Contains( "$PER_DEPLOYMENT" ) );
        central.Builders.Single().Configuration["App:Demo:Password"].ShouldBe( "pass" );
    }

    [Test]
    public void the_folders_are_the_identity_of_the_deployments_and_the_overlays_are_merge_patches()
    {
        var central = Create( _central,
                              ("#Production", """
                                    {
                                      "Builders": { "Front/Front.Builder": { "App": { "Local": { "HttpEndpoint": { "Urls": "https://*:443" } }, "Demo": null } } },
                                      "Links": [ { "From": "$Front", "To": "$Auth3", "Link": { "Address": "tcp:auth3:37300", "AutoTrustKey": "Never" } } ]
                                    }
                                    """),
                              ("#Staging", """{ "$BASE": "#Production", "Defaults": { "App": { "Logging": null } } }"""),
                              ("Paris/Shop/#Production", """{ "Builders": { "Front/Front.Builder": { "App": { "Demo": { "Password": "other" } } } } }"""),
                              ("Assets/#Production", null!),
                              ("Docs", null!),
                              (".build/Production/#Ignored", null!) );
        var deployments = Load( central );
        deployments.Select( d => d.Name ).ShouldBe( ["#Production", "#Staging", "Paris/Shop/#Production"] );

        var production = deployments[0];
        production.Configuration.Configuration["Domain"].ShouldBe( "Sample/Auth3" );
        production.Configuration.Configuration["Environment"].ShouldBe( "#Production" );
        production.Configuration.Configuration["Builders:Front/Front.Builder:App:Local:HttpEndpoint:Urls"].ShouldBe( "https://*:443" );
        production.Configuration.Configuration.TryGetSection( "Builders:Front/Front.Builder:App:Demo" ).ShouldBeNull();
        production.Configuration.Configuration["Defaults:App:Logging:LogLevel:Default"].ShouldBe( "Warning" );

        var staging = deployments[1];
        staging.Base.ShouldBeSameAs( production );
        staging.Configuration.Configuration["Environment"].ShouldBe( "#Staging" );
        staging.Configuration.Configuration["Links:0:Link:Address"].ShouldBe( "tcp:auth3:37300" );
        staging.Configuration.Configuration.TryGetSection( "Defaults:App:Logging" ).ShouldBeNull();

        var paris = deployments[2];
        paris.Configuration.Configuration["Domain"].ShouldBe( "Paris/Shop" );
        paris.Configuration.Configuration["Builders:Front/Front.Builder:App:Demo:Password"].ShouldBe( "other" );
        paris.Configuration.Configuration["Builders:Front/Front.Builder:App:Local:HttpEndpoint:Urls"].ShouldBe( "http://localhost:5300" );

        // The merged file is written when it changes.
        staging.WriteMergedFile( TestHelper.Monitor, central ).ShouldBeTrue();
        var merged = staging.Folder.AppendPart( CentralDeployment.MergedFileName );
        var text = File.ReadAllText( merged );
        text.ShouldStartWith( "// Generated from ckonf.json, Deployment/#Production/ckonf.json, Deployment/#Staging/ckonf.json (from the root of the solution): do not edit." );
        text.ShouldNotContain( "$PER_DEPLOYMENT" );
        text.ShouldNotContain( "$BASE" );
        var time = File.GetLastWriteTimeUtc( merged );
        staging.WriteMergedFile( TestHelper.Monitor, central ).ShouldBeTrue();
        File.GetLastWriteTimeUtc( merged ).ShouldBe( time );
    }

    [Test]
    public void the_values_per_deployment_are_written_by_the_deployment_itself()
    {
        var central = Create( _central,
                              ("#Production", """
                                    {
                                      "Builders": { "Front/Front.Builder": { "App": { "Local": { "HttpEndpoint": { "Urls": "https://*:443" } }, "Demo": null } } },
                                      "Links": [ { "From": "$Front", "To": "$Auth3", "Link": { "Address": "tcp:auth3:37300", "AutoTrustKey": "Never" } } ]
                                    }
                                    """),
                              ("#Staging", """{ "$BASE": "#Production" }""") );
        var deployments = Load( central );
        var appKeys = new Dictionary<string, IReadOnlyList<(string, string)>>
        {
            { "Front", [("Local:HttpEndpoint:Urls", "[ConfigurationPerDeployment( \"Party:Urls\" )] of 'HttpEndpointFeatureDriver'"), ("Logging:LogLevel:Default", "a test")] }
        };
        var builder = central.RootFolder.Combine( "Front/Front.Builder" );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            // The "Defaults:App" value is inherited by the App: not written by #Production.
            deployments[0].CheckPerDeployment( TestHelper.Monitor, central, builder, appKeys ).ShouldBeFalse();
            texts.Count( t => t.Contains( "inherits" ) ).ShouldBe( 1 );
            texts.ShouldContain( t => t.Contains( "The deployment '#Production' inherits \"Defaults:App:Logging:LogLevel:Default\" (a test)" ) );
        }
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            // #Staging writes nothing: everything it inherits from #Production is an error.
            deployments[1].CheckPerDeployment( TestHelper.Monitor, central, builder, appKeys ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "inherits \"Links:0:Link:AutoTrustKey\" (\"$PER_DEPLOYMENT\" of" ) );
            texts.ShouldContain( t => t.Contains( "inherits \"Builders:Front/Front.Builder:App:Local:HttpEndpoint:Urls\" ([ConfigurationPerDeployment" ) );
            // "Demo" is removed by #Production: nothing to inherit.
            texts.ShouldNotContain( t => t.Contains( "App:Demo" ) );
        }
        // Without the "Defaults:App" key, #Production is fine: its array of links replaces the central one whole.
        deployments[0].CheckPerDeployment( TestHelper.Monitor, central, builder, new Dictionary<string, IReadOnlyList<(string, string)>> { { "Front", [("Local:HttpEndpoint:Urls", "x")] } } )
                      .ShouldBeTrue();
    }

    [TestCase( """{ "Domain": "X" }""", "\"Domain\" can't be written" )]
    [TestCase( """{ "GlobalDomainPrefix": "X" }""", "\"GlobalDomainPrefix\" can't be written" )]
    [TestCase( """{ "$BASE": "#Unknown" }""", "there is no such deployment" )]
    [TestCase( """{ "$BASE": 3 }""", "Invalid \"$BASE\"" )]
    [TestCase( """{ "$PER_DEPLOYMENT": "Demo" }""", "an array of patterns" )]
    [TestCase( """{ "Builders": null }""", "must define \"Builders\"" )]
    public void invalid_overlays( string overlay, string message )
    {
        ShouldFail( Create( _central, ("#Production", overlay) ), message );
    }

    [Test]
    public void invalid_deployments()
    {
        ShouldFail( Create( _central, ("#A", """{ "$BASE": "#B" }"""), ("#B", """{ "$BASE": "#A" }""") ), "overlay each other" );
        ShouldFail( Create( _central, ("#Dev", "{}") ), "the development (\"#Dev\") is the central configuration" );
        ShouldFail( Create( _central, ("Sample/Auth3/#Production", "{}") ), "spells the central domain 'Sample/Auth3'" );
        ShouldFail( Create( _central, ("Paris.Shop/#Production", "{}") ), "a part of a domain has no '.'" );
        ShouldFail( Create( _central, ("#Production", null!) ), "has no 'ckonf.json'" );
    }

    [Test]
    public void the_package_declarations_are_App_patterns_on_the_anchors_and_their_fallbacks()
    {
        var engine = new EngineConfiguration();
        var app = new AppConfiguration( "Front", "C:/Front.App", "C:/Front.Host", ImmutableConfigurationSection.CreateFromJson( "Apps", "Front", "{}" ), engine.FirstBinPath );
        app.DeclareAnchor( "Local", ["Party"], ["Defaults"] );
        app.DeclareAnchor( "Parties:0", ["Party", "Remote"], ["Defaults"] );
        app.AddPerDeploymentKey( "StoreRootPath" );
        AppBuilderConfiguration.CollectPerDeploymentKeys( TestHelper.Monitor,
                                                          app,
                                                          [new PackageConfigurationPerDeployment( "HttpEndpointFeatureDriver", ["HttpEndpoint"], "Party:Urls" )],
                                                          [],
                                                          out var keys ).ShouldBeTrue();
        keys.Select( k => k.Pattern ).ShouldBe( ["StoreRootPath", "Local:HttpEndpoint:Urls", "Defaults:HttpEndpoint:Urls", "Parties:[*]:HttpEndpoint:Urls"] );
        AppBuilderConfiguration.CollectPerDeploymentKeys( TestHelper.Monitor, app, [new PackageConfigurationPerDeployment( "X", ["X"], "Urls" )], [], out _ )
                               .ShouldBeFalse();
    }

    [Test]
    public void the_party_keys_are_App_patterns_on_the_anchors_of_their_kind()
    {
        var engine = new EngineConfiguration();
        var app = new AppConfiguration( "Front", "C:/Front.App", "C:/Front.Host", ImmutableConfigurationSection.CreateFromJson( "Apps", "Front", "{}" ), engine.FirstBinPath );
        app.DeclareAnchor( "Local", ["Party"], ["Defaults"] );
        app.DeclareAnchor( "Parties:0", ["Party", "Remote"], ["Defaults"] );
        app.DeclareAnchor( "Parties:1", ["Party", "Remote", "RemoteTemplate"] );
        app.AddPerDeploymentPartyKey( "Remote", "Address" );
        app.AddPerDeploymentPartyKey( "remote", "address" );
        AppBuilderConfiguration.CollectPerDeploymentKeys( TestHelper.Monitor, app, [], [], out var keys ).ShouldBeTrue();
        keys.Select( k => k.Pattern ).ShouldBe( ["Parties:[*]:Address", "Defaults:Address"] );
    }

    [Test]
    public void the_size_of_a_fleet_is_per_deployment()
    {
        var engine = new EngineConfiguration();
        var fleet = new AppConfiguration( "Trolley",
                                          "C:/Trolley.App",
                                          "C:/Trolley.Host",
                                          ImmutableConfigurationSection.CreateFromJson( "Apps", "Trolley", """{ "FullName": "Sample/$Trolley-{Index}", "InstanceCount": 2 }""" ),
                                          engine.FirstBinPath );
        AppBuilderConfiguration.CollectPerDeploymentKeys( TestHelper.Monitor, fleet, [], [], out var keys ).ShouldBeTrue();
        keys.Select( k => k.Pattern ).ShouldBe( ["InstanceCount", "Instances", "MaxInstanceCount"] );

        var single = new AppConfiguration( "Front", "C:/Front.App", "C:/Front.Host", ImmutableConfigurationSection.CreateFromJson( "Apps", "Front", "{}" ), engine.FirstBinPath );
        AppBuilderConfiguration.CollectPerDeploymentKeys( TestHelper.Monitor, single, [], [], out keys ).ShouldBeTrue();
        keys.ShouldBeEmpty();
    }

    [Test]
    public void merge_patch_follows_RFC_7396()
    {
        var target = System.Text.Json.Nodes.JsonNode.Parse( """{ "a": "b", "c": { "d": "e", "f": "g" }, "arr": [1, 2] }""",
                                                             new System.Text.Json.Nodes.JsonNodeOptions { PropertyNameCaseInsensitive = true } );
        var patch = System.Text.Json.Nodes.JsonNode.Parse( """{ "A": "z", "c": { "f": null, "h": [ { "i": 1 } ] }, "arr": [3] }""" );
        CentralDeployment.MergePatch( target, patch )!.ToJsonString()
                         .ShouldBe( """{"a":"z","c":{"d":"e","h":[{"i":1}]},"arr":[3]}""" );
    }

    const string _twoBuilders = """
        {
          "Domain": "Sample/Auth3",
          "Deployment": { "Publish": { "SelfContained": false, "PublishSingleFile": true, "InvariantGlobalization": null } },
          "Builders": {
            "Front/Front.Builder": { "App": { "AppProject": "../Other/Front.App", "HostProject": "../Front.Host" } },
            "Auth3/Auth3.Builder": { "App": {} }
          }
        }
        """;

    [Test]
    public void the_settings_of_the_deployments_and_their_asset_names()
    {
        var central = Create( _twoBuilders,
                              ("#Production", """
                                    {
                                      "Deployment": {
                                        "Builders": [ "Front/Front.Builder" ],
                                        "SingleZipAsset": true,
                                        "Publish": { "RuntimeIdentifier": "linux-x64", "PublishSingleFile": null },
                                        "Apps": { "front": { "Publish": { "SelfContained": true, "TieredPGO": 1, "RuntimeIdentifier": "" } } }
                                      }
                                    }
                                    """),
                              ("Paris/Shop/#Staging", "{}") );
        var deployments = Load( central );
        var production = deployments.Single( d => d.Name == "#Production" );
        production.Settings.Builders.ShouldBe( ["Front/Front.Builder"] );
        // The central defaults, the overlay (null removes an inherited property), then the App ("" resets one).
        production.Settings.Publish.OrderBy( kv => kv.Key ).Select( kv => $"{kv.Key}={kv.Value}" )
                  .ShouldBe( ["RuntimeIdentifier=linux-x64", "SelfContained=false"] );
        production.Settings.GetPublishProperties( "Front" ).Select( p => $"{p.Name}={p.Value}" )
                  .ShouldBe( ["RuntimeIdentifier=", "SelfContained=true", "TieredPGO=1"] );
        production.Settings.GetPublishProperties( "Auth3" ).Select( p => $"{p.Name}={p.Value}" )
                  .ShouldBe( ["RuntimeIdentifier=linux-x64", "SelfContained=false"] );
        production.DeployedBuilders.Select( b => b.Path ).ShouldBe( ["Front/Front.Builder"] );
        production.AssetName.ShouldBe( "Production" );
        production.GetAssetFolder( "Front" ).ShouldBe( "Production/Front" );
        production.Environment.ShouldBe( "#Production" );

        var staging = deployments.Single( d => d.Name == "Paris/Shop/#Staging" );
        staging.Settings.Builders.ShouldBeNull();
        staging.Settings.GetPublishProperties( "Front" ).Select( p => $"{p.Name}={p.Value}" )
               .ShouldBe( ["PublishSingleFile=true", "SelfContained=false"] );
        staging.DeployedBuilders.Select( b => b.Path ).ShouldBe( ["Front/Front.Builder", "Auth3/Auth3.Builder"] );
        staging.AssetName.ShouldBe( "Paris.Shop.Staging" );
        staging.GetAssetFolder( "Front" ).ShouldBe( "Paris.Shop.Staging-Front" );
        staging.Environment.ShouldBe( "#Staging" );
    }

    [TestCase( """{ "Deployment": { "Builders": [ "Nope/Nope.Builder" ] } }""", "Invalid builder '\"Nope/Nope.Builder\"'" )]
    [TestCase( """{ "Deployment": { "Publish": { "Bad Name": 1 } } }""", "Invalid MSBuild property name 'Bad Name'" )]
    [TestCase( """{ "Deployment": { "SingleZipAsset": "yes" } }""", "Invalid \"Deployment:SingleZipAsset\"" )]
    [TestCase( """{ "Deployment": { "Publish": { "X": [ 1 ] } } }""", "Invalid value of 'X'" )]
    [TestCase( """{ "Deployment": { "Apps": { "Nope": { "Publish": {} } } } }""", "Unknown App 'Nope'" )]
    [TestCase( """{ "Deployment": { "Apps": { "Front": { "Other": {} } } } }""", "an object with \"Publish\" is expected" )]
    [TestCase( """{ "Deployment": { "Other": 1 } }""", "Unknown key \"Deployment:Other\"" )]
    [TestCase( """{ "Deployment": [] }""", "an object is expected" )]
    public void invalid_settings( string overlay, string message )
    {
        ShouldFail( Create( _twoBuilders, ("#Production", overlay) ), message );
    }

    [Test]
    public void the_manifest_of_a_fleet_lists_its_instances()
    {
        var production = Load( Create( _twoBuilders, ("#Production", "{}") ) ).Single();
        NormalizedPath root = Path.Combine( Path.GetTempPath(), "CK.AppBuilder.Tests", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( root );
        File.WriteAllText( root.AppendPart( "Sample.slnx" ), "<Solution />" );
        var builder = root.AppendPart( "Backs.Builder" );
        Directory.CreateDirectory( builder );
        File.WriteAllText( builder.AppendPart( "ckonf.json" ), """
            {
              "App": {
                "FullName": "Sample/$Backs-{Name}/#Production",
                "InstanceCount": 2,
                "MaxInstanceCount": 4,
                "Local": { "Port": "tcp:0.0.0.0:{38000+Index}" },
                "Db": { "$SECRET": "Db" },
                "Key": { "$SECRET": "Backs:{Name}:Key" },
                "Ticket": { "$SECRET?": "Enlist:{Name}" },
                "Other": { "$SECRET?": "Db" }
              }
            }
            """ );
        Directory.CreateDirectory( root.AppendPart( "Backs.App" ) );
        Directory.CreateDirectory( root.AppendPart( "Backs.Host" ) );
        var app = AppBuilderConfiguration.Load( TestHelper.Monitor, builder, "bin/Debug/net10.0", null, out _ ).ShouldNotBeNull();
        var keys = new Dictionary<string, IReadOnlyList<(string, string)>> { { "Backs", [("Local:Port", "test")] } };
        app.WriteDeploymentManifests( TestHelper.Monitor, production, keys ).ShouldBeTrue();

        var m = System.Text.Json.Nodes.JsonNode.Parse( File.ReadAllText( root.Combine( "Backs.Host/$StObjGen/Deployment.manifest" ) ) )!;
        m["FullName"]!.GetValue<string>().ShouldBe( "Sample/$Backs-{Name}/#Production" );
        m["Deployment"]!.GetValue<string>().ShouldBe( "#Production" );
        m["Publish"]!["PublishSingleFile"]!.GetValue<string>().ShouldBe( "true" );
        m["Secrets"]!.AsArray().Select( s => s!.GetValue<string>() ).ShouldBe( ["Backs:{Name}:Key", "Db"] );
        // An optional secret also required is required.
        m["OptionalSecrets"]!.AsArray().Select( s => s!.GetValue<string>() ).ShouldBe( ["Enlist:{Name}"] );
        m["PerDeployment"]!["Local:Port"]!.GetValue<string>().ShouldBe( "tcp:0.0.0.0:{38000+Index}" );
        var configured = m["Instances"]!["Configured"]!.AsArray();
        configured.Count.ShouldBe( 2 );
        configured[1]!["Name"]!.GetValue<string>().ShouldBe( "2" );
        configured[1]!["Index"]!.GetValue<int>().ShouldBe( 2 );
        configured[1]!["FullName"]!.GetValue<string>().ShouldBe( "Sample/$Backs-2/#Production" );
        configured[1]!["Arguments"]![0]!.GetValue<string>().ShouldBe( "--Instance=2" );
        configured[1]!["Secrets"]!.AsArray().Select( s => s!.GetValue<string>() ).ShouldBe( ["Backs:2:Key", "Db"] );
        configured[1]!["OptionalSecrets"]!.AsArray().Select( s => s!.GetValue<string>() ).ShouldBe( ["Enlist:2"] );
        configured[1]!["PerDeployment"]!["Local:Port"]!.GetValue<string>().ShouldBe( "tcp:0.0.0.0:38002" );
        m["Instances"]!["Dynamic"]!["From"]!.GetValue<int>().ShouldBe( 3 );
        m["Instances"]!["Dynamic"]!["To"]!.GetValue<int>().ShouldBe( 4 );
    }

    // What the AppIdentity contributor claims.
    sealed class IdentityContributor : IAppBuilderContributor
    {
        public bool Contribute( IActivityMonitor monitor, AppBuilderContext context ) => true;

        public bool ProjectFromCentral( IActivityMonitor monitor, CentralProjectionContext context )
        {
            context.ClaimKey( "Domain" );
            context.ClaimKey( "Environment" );
            return true;
        }
    }

    [Test]
    public void the_builder_of_a_deployment_is_projected_on_the_original_App()
    {
        var central = Create( _twoBuilders, ("#Production", """{ "Builders": { "Front/Front.Builder": { "App": { "Demo": 1 } } } }""") );
        var production = Load( central ).Single();
        var folder = central.RootFolder.Combine( "Deployment/.build/Production/Front.Builder" );
        Directory.CreateDirectory( folder );
        production.WriteBuilderProjection( TestHelper.Monitor, central, "Front/Front.Builder", folder, [new IdentityContributor()] ).ShouldBeTrue();
        var text = File.ReadAllText( folder.AppendPart( "ckonf.json" ) );
        text.ShouldStartWith( "// Generated from Deployment/#Production/ckonf.merged.json (the deployment '#Production'): do not edit." );
        var projected = System.Text.Json.Nodes.JsonNode.Parse( text, documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip } )!;
        projected["App"]!["AppProject"]!.GetValue<string>().ShouldBe( "../../../../Front/Other/Front.App" );
        projected["App"]!["HostProject"].ShouldBeNull();
        projected["App"]!["Demo"]!.GetValue<int>().ShouldBe( 1 );

        production.WriteBuilderFile( folder, "Front/Front.Builder" );
        CentralDeployment.TryReadBuilderFile( TestHelper.Monitor, folder, out var name, out var builder ).ShouldBeTrue();
        name.ShouldBe( "#Production" );
        builder.ShouldBe( "Front/Front.Builder" );

        production.WriteBuilderProjection( TestHelper.Monitor, central, "Nope/Nope.Builder", folder, [new IdentityContributor()] ).ShouldBeFalse();
    }
}
