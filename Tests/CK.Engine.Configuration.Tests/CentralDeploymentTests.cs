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
                                                          out var keys ).ShouldBeTrue();
        keys.Select( k => k.Pattern ).ShouldBe( ["StoreRootPath", "Local:HttpEndpoint:Urls", "Defaults:HttpEndpoint:Urls", "Parties:0:HttpEndpoint:Urls"] );
        AppBuilderConfiguration.CollectPerDeploymentKeys( TestHelper.Monitor, app, [new PackageConfigurationPerDeployment( "X", ["X"], "Urls" )], out _ )
                               .ShouldBeFalse();
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
}
