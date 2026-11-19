using CK.Core;
using CK.Setup;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppBuilder.Tests;

[TestFixture]
public class ConfigurationLockTests
{
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

    static AppBuilderConfiguration Load( string ckonf )
    {
        var builder = CreateSolution( ckonf, "Backs" );
        return AppBuilderConfiguration.Load( TestHelper.Monitor, builder, "bin/Debug/net10.0", null, out _ ).ShouldNotBeNull();
    }

    // What the AppIdentity contributor declares: the party keys are read at start, projected in "CK-AppIdentity"; the
    // local party and the remotes are anchors whose values may come from the "Defaults".
    static void DeclareParty( AppConfiguration a )
    {
        foreach( var k in new[] { "FullName", "Local", "Parties", "Defaults" } )
        {
            a.ProjectInSection( k, "CK-AppIdentity" );
            a.DeclareInitSection( k );
        }
        a.DeclareAnchor( "Local", ["Party"], ["Defaults"] );
        a.DeclareAnchor( "Parties:0", ["Party", "Remote"], ["Defaults"] );
        a.DeclareAnchor( "Parties:1", ["Party", "Remote", "RemoteTemplate", "OpenedRemoteTemplate"], ["Defaults"] );
    }

    const string _party = """
        "FullName": "Sample/$Front",
        "Defaults": { "KeyManagement": { "AutoTrustKey": "Once" } },
        "Local": { "HttpEndpoint": { "Kestrel": { "Endpoints": { "Http": { "Url": "http://*:80" },
                                                                  "Https": { "Url": "https://*:443", "Certificate": { "Path": "c.pfx" } } } } } },
        "Parties": [
          { "PartyName": "$Back", "Address": "tcp:127.0.0.1:3712" },
          { "PartyName": "$Worker-{Name}", "MaxInstanceCount": 5, "KeyManagement": { "Rotation": "10" } }
        ]
        """;

    [Test]
    public void the_LOCK_are_removed_and_a_Compile_value_is_burnt_instead_of_projected()
    {
        var app = Load( $$"""
            {
              "App": {
                {{_party}},
                "$LOCK": { "Parties:[*]:Address": "Compile" },
                "Logging": { "LogLevel": { "Default": "Warning" } }
              }
            }
            """ );
        var front = app.Apps.Single();
        front.Locks.Count.ShouldBe( 1 );
        front.Configuration.TryGetSection( "$LOCK" ).ShouldBeNull();
        DeclareParty( front );
        app.ApplyLocks( TestHelper.Monitor ).ShouldBeTrue();

        front.BinPathConfiguration.CompiledValues.Count.ShouldBe( 1 );
        front.BinPathConfiguration.CompiledValues["CK-AppIdentity:Parties:0:Address"].ShouldBe( "tcp:127.0.0.1:3712" );
        front.BinPathConfiguration.CompiledLocks.ShouldBe( ["CK-AppIdentity:Parties:[*]:Address"] );
        // A value in an array's item is in an object: removed from the projection.
        app.WriteProjections( TestHelper.Monitor ).ShouldBeTrue();
        var text = File.ReadAllText( front.HostProjectPath.AppendPart( "appsettings.json" ) );
        text.ShouldNotContain( "$LOCK" );
        text.ShouldNotContain( "tcp:127.0.0.1:3712" );
        text.ShouldContain( "\"PartyName\": \"$Back\"" );
    }

    [Test]
    public void a_LOCK_in_the_Defaults_locks_the_resolved_value_of_every_party_below()
    {
        var app = Load( $$"""
            {
              "App": {
                {{_party}}
              }
            }
            """ );
        var front = app.Apps.Single();
        DeclareParty( front );
        // As written in "Defaults": { "$LOCK": { ... } }.
        front.AddLock( new ConfigurationLockEntry( $"{front.Configuration.Path}:Defaults", "KeyManagement:AutoTrustKey", "Compile" ) );
        app.ApplyLocks( TestHelper.Monitor ).ShouldBeTrue();

        // Burnt at the path of each party: a party's own key in the deployed file can't override it.
        var values = front.BinPathConfiguration.CompiledValues;
        values["CK-AppIdentity:Local:KeyManagement:AutoTrustKey"].ShouldBe( "Once" );
        values["CK-AppIdentity:Parties:0:KeyManagement:AutoTrustKey"].ShouldBe( "Once" );
        values["CK-AppIdentity:Parties:1:KeyManagement:AutoTrustKey"].ShouldBe( "Once" );
        values.Count.ShouldBe( 3 );
    }

    [Test]
    public void a_package_restriction_requires_its_value_on_the_parties_of_its_kind()
    {
        var app = Load( $$"""
            {
              "App": {
                {{_party}}
              }
            }
            """ );
        var front = app.Apps.Single();
        DeclareParty( front );
        var restriction = new PackageConfigurationLock( "KeyManagementFeatureDriver",
                                                        "KeyManagement",
                                                        new ConfigurationLockAttribute( "OpenedRemoteTemplate:AutoTrustKey", ConfigurationLevel.Compile ) { Value = "Never" } );
        app.ApplyLocks( TestHelper.Monitor, a => [restriction] ).ShouldBeTrue();
        // The "Defaults" value ("Once") is replaced on the Opened template only.
        front.BinPathConfiguration.CompiledValues.Count.ShouldBe( 1 );
        front.BinPathConfiguration.CompiledValues["CK-AppIdentity:Parties:1:KeyManagement:AutoTrustKey"].ShouldBe( "Never" );

        // An explicit other value is an error.
        var invalid = Load( """
            {
              "App": {
                "FullName": "Sample/$Front",
                "Parties": [ { "PartyName": "$Worker-{Name}", "MaxInstanceCount": 5, "KeyManagement": { "AutoTrustKey": "Always" } } ]
              }
            }
            """ );
        var a = invalid.Apps.Single();
        a.DeclareInitSection( "Parties" );
        a.DeclareAnchor( "Parties:0", ["Party", "OpenedRemoteTemplate"] );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            invalid.ApplyLocks( TestHelper.Monitor, _ => [restriction] ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "\"ckonf:App:Parties:0:KeyManagement:AutoTrustKey\" is 'Always'" ) && t.Contains( "requires 'Never'" ) );
        }
    }

    [Test]
    public void a_literal_LOCK_on_an_absent_value_burns_its_absence()
    {
        var app = Load( $$"""
            {
              "App": {
                {{_party}},
                "Local": { "$LOCK": { "HttpEndpoint:ForwardedHeaders": "Compile" } }
              }
            }
            """.Replace( "\"Local\": { \"HttpEndpoint\"", "\"LocalX\": { \"HttpEndpoint\"" ) );
        var front = app.Apps.Single();
        DeclareParty( front );
        app.ApplyLocks( TestHelper.Monitor ).ShouldBeTrue();
        front.BinPathConfiguration.CompiledValues.ShouldContainKeyAndValue( "CK-AppIdentity:Local:HttpEndpoint:ForwardedHeaders", null );
        front.BinPathConfiguration.CompiledLocks.ShouldContain( "CK-AppIdentity:Local:HttpEndpoint:ForwardedHeaders" );
    }

    [TestCase( "\"Logging:LogLevel:Default\": \"Compile\"", "that no code declares" )]
    [TestCase( "\"Parties:0:Address\": \"Compile\"", "has an index" )]
    [TestCase( "\"Parties:[*]:Address\": \"Never\"", "Invalid level" )]
    [TestCase( "\"Parties:[*]:Address\": \"Run\"", "its code supports Init only" )]
    [TestCase( "\"Name\": \"Init\"", "its code supports Compile only" )]
    public void invalid_LOCK( string entry, string error )
    {
        var app = Load( $$"""
            {
              "App": {
                {{_party}},
                "Logging": { "LogLevel": { "Default": "Warning" } },
                "$LOCK": { {{entry}} }
              }
            }
            """ );
        var front = app.Apps.Single();
        DeclareParty( front );
        front.AddCompileKey( "Name" );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            app.ApplyLocks( TestHelper.Monitor ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( error ) );
        }
    }

    [Test]
    public void a_LOCK_of_a_Builder_aspect_burns_what_it_projects()
    {
        var app = Load( """
            {
              "Aspects": { "SqlServer": { "DefaultDatabaseConnectionString": "Server=.;Database=Db", "$LOCK": { "DefaultDatabaseConnectionString": "Compile" } } },
              "App": { "FullName": "Sample/$Front" }
            }
            """ );
        app.BuilderLocks.Count.ShouldBe( 1 );
        var front = app.Apps.Single();
        // What the SqlServer aspect does.
        front.ProjectValue( "SqlServer:DefaultDatabaseConnectionString", "Server=.;Database=Db", "ckonf:Aspects:SqlServer:DefaultDatabaseConnectionString" );
        app.ApplyLocks( TestHelper.Monitor ).ShouldBeTrue();
        front.BinPathConfiguration.CompiledValues.Count.ShouldBe( 1 );
        front.BinPathConfiguration.CompiledValues["SqlServer:DefaultDatabaseConnectionString"].ShouldBe( "Server=.;Database=Db" );
        app.WriteProjections( TestHelper.Monitor ).ShouldBeTrue();
        File.ReadAllText( front.HostProjectPath.AppendPart( "appsettings.json" ) ).ShouldNotContain( "Database=Db" );
    }

    [TestCase( "Parties:[*]:Address", "Parties:3:Address", true )]
    [TestCase( "Parties:[*]:Address", "Parties:X:Address", false )]
    [TestCase( "Endpoints:*:Url", "endpoints:Http:url", true )]
    [TestCase( "Endpoints:*:Url", "Endpoints:0:Url", false )]
    [TestCase( "Endpoints:Http", "Endpoints:Http:Url", true )]
    [TestCase( "Endpoints:Http:Url", "Endpoints:Http", false )]
    public void the_patterns( string pattern, string path, bool covers )
    {
        ConfigurationPathPattern.TryParse( pattern, out var p, out _ ).ShouldBeTrue();
        p.Covers( path ).ShouldBe( covers );
    }
}
