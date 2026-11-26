using CK.Core;
using CK.Setup;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.AppBuilder.Tests;

[TestFixture]
public class ConfigurationSecretTests
{
    static AppContextConfiguration Load( string ckonf )
    {
        NormalizedPath root = Path.Combine( Path.GetTempPath(), "CK.AppBuilder.Tests", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( root );
        File.WriteAllText( root.AppendPart( "Sample.slnx" ), "<Solution />" );
        var builder = root.AppendPart( "Backs.Builder" );
        Directory.CreateDirectory( builder );
        File.WriteAllText( builder.AppendPart( "ckonf.json" ), ckonf );
        Directory.CreateDirectory( root.AppendPart( "Backs.App" ) );
        Directory.CreateDirectory( root.AppendPart( "Backs.Host" ) );
        return AppContextConfiguration.Load( TestHelper.Monitor, builder, "bin/Debug/net10.0", null, out _ ).ShouldNotBeNull();
    }

    static void DeclareParty( AppConfiguration a )
    {
        foreach( var k in new[] { "FullName", "Local", "Defaults" } )
        {
            a.ProjectInSection( k, "CK-AppIdentity" );
            a.DeclareInitSection( k );
        }
        a.DeclareAnchor( "Local", ["Party"], ["Defaults"] );
    }

    [Test]
    public void a_handle_is_projected_as_is_and_a_Compile_lock_burns_the_handle_not_a_value()
    {
        var app = Load( """
            {
              "App": {
                "FullName": "Sample/$Back",
                "Local": { "Oidc": { "Duende": { "ClientSecret": { "$SECRET": "Duende:ClientSecret" } },
                                     "Google": { "ClientSecret": { "$SECRET": "Google:ClientSecret" } } } },
                "$LOCK": { "Local:Oidc:Google:ClientSecret": "Compile" }
              }
            }
            """ );
        var back = app.Apps.Single();
        DeclareParty( back );
        app.ApplyLocks( TestHelper.Monitor ).ShouldBeTrue();
        app.CheckSecrets( TestHelper.Monitor ).ShouldBeTrue();

        back.BinPathConfiguration.CompiledValues.Count.ShouldBe( 1 );
        back.BinPathConfiguration.CompiledValues["CK-AppIdentity:Local:Oidc:Google:ClientSecret:$SECRET"].ShouldBe( "Google:ClientSecret" );
        app.WriteProjections( TestHelper.Monitor ).ShouldBeTrue();
        var text = File.ReadAllText( back.HostProjectPath.AppendPart( "appsettings.json" ) );
        text.ShouldContain( "\"$SECRET\": \"Duende:ClientSecret\"" );
        text.ShouldNotContain( "Google:ClientSecret" );
    }

    [Test]
    public void an_optional_handle_is_burnt_as_an_optional_handle()
    {
        var app = Load( """
            {
              "App": {
                "FullName": "Sample/$Back",
                "Local": { "Ticket": { "$SECRET?": "Enlist:Back" }, "Key": { "$SECRET": "Back:Key" } },
                "$LOCK": { "Local:Ticket": "Compile", "Local:Key": "Compile" }
              }
            }
            """ );
        var back = app.Apps.Single();
        DeclareParty( back );
        app.ApplyLocks( TestHelper.Monitor ).ShouldBeTrue();
        app.CheckSecrets( TestHelper.Monitor ).ShouldBeTrue();
        back.BinPathConfiguration.CompiledValues["CK-AppIdentity:Local:Ticket:$SECRET?"].ShouldBe( "Enlist:Back" );
        back.BinPathConfiguration.CompiledValues["CK-AppIdentity:Local:Key:$SECRET"].ShouldBe( "Back:Key" );
    }

    [Test]
    public void a_projected_handle_is_burnt_by_the_lock_of_the_value_it_replaces()
    {
        var app = Load( """
            {
              "Aspects": {
                "SqlServer": {
                  "DefaultDatabaseConnectionString": { "$SECRET": "Db" },
                  "$LOCK": { "DefaultDatabaseConnectionString": "Compile" }
                }
              },
              "App": { "FullName": "Sample/$Back" }
            }
            """ );
        var back = app.Apps.Single();
        // What the SqlServer aspect does with a handle: it projects the handle, from the origin of the handle.
        back.ProjectValue( "SqlServer:DefaultDatabaseConnectionString:$SECRET",
                           "Db",
                           $"{app.Aspects.Section!.Path}:SqlServer:DefaultDatabaseConnectionString:$SECRET" );
        app.ApplyLocks( TestHelper.Monitor ).ShouldBeTrue();
        back.BinPathConfiguration.CompiledValues["SqlServer:DefaultDatabaseConnectionString:$SECRET"].ShouldBe( "Db" );
    }

    [TestCase( """{ "$SECRET": "Bad Name" }""", "Invalid secret name" )]
    [TestCase( """{ "$SECRET": "A::B" }""", "Invalid secret name" )]
    [TestCase( """{ "$SECRET": { "Name": "A" } }""", "Invalid secret name" )]
    [TestCase( """{ "$SECRET": "A", "Other": 3 }""", "must be its only key" )]
    [TestCase( """{ "$SECRET": "Back:{Name}:Key" }""", "has a placeholder: only the handles of a fleet App" )]
    [TestCase( """{ "$SECRET?": "Bad Name" }""", "Invalid secret name" )]
    [TestCase( """{ "$SECRET?": "A", "Other": 3 }""", "\"$SECRET?\" must be its only key" )]
    public void an_invalid_handle_is_an_error( string handle, string message )
    {
        var app = Load( $$"""
            {
              "App": { "FullName": "Sample/$Back", "Local": { "Secret": {{handle}} } }
            }
            """ );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            app.CheckSecrets( TestHelper.Monitor ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( message ) );
        }
    }

    [Test]
    public void a_plain_value_where_a_secret_is_declared_is_reported()
    {
        var app = Load( """
            {
              "App": {
                "FullName": "Sample/$Back",
                "Defaults": { "Oidc": { "Duende": { "ClientSecret": "secret" } } },
                "Local": { "Oidc": { "Google": { "ClientSecret": { "$SECRET": "Google:ClientSecret" } } } },
                "Demo": { "Password": "pass" }
              }
            }
            """ );
        var back = app.Apps.Single();
        DeclareParty( back );
        back.AddSecretKey( "Demo:Password" );
        var declared = new PackageConfigurationSecret( "AuthCenterOidcFeatureDriver", ["Oidc"], new ConfigurationSecretAttribute( "Party:*:ClientSecret" ) );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            app.CheckSecrets( TestHelper.Monitor, _ => [declared] ).ShouldBeTrue();
            texts.Count( t => t.Contains( "is a secret" ) ).ShouldBe( 2 );
            texts.ShouldContain( t => t.Contains( "Demo:Password\" is a secret (the secret key 'Demo:Password')" ) );
            texts.ShouldContain( t => t.Contains( "Defaults:Oidc:Duende:ClientSecret\" is a secret ([ConfigurationSecret( \"Party:*:ClientSecret\" )]" ) );
        }
    }

    [Test]
    public void a_declaration_without_the_kind_of_the_parties_is_an_error()
    {
        var app = Load( """{ "App": { "FullName": "Sample/$Back" } }""" );
        var declared = new PackageConfigurationSecret( "X", ["Oidc"], new ConfigurationSecretAttribute( "ClientSecret" ) );
        app.CheckSecrets( TestHelper.Monitor, _ => [declared] ).ShouldBeFalse();
    }

    [Test]
    public void the_secrets_file_is_flattened_into_names()
    {
        NormalizedPath root = Path.Combine( Path.GetTempPath(), "CK.AppBuilder.Tests", Guid.NewGuid().ToString( "N" ) );
        Directory.CreateDirectory( root );
        var file = root.AppendPart( ConfigurationSecret.DevFileName );
        File.WriteAllText( file, """
            {
              // The development secrets.
              "Duende": { "ClientSecret": "secret" },
              "Db": "Server=.;Database=X;Integrated Security=True",
              "Port:Number": 3712,
            }
            """ );
        ConfigurationSecret.TryReadFile( file, out var secrets, out var error ).ShouldBeTrue( error );
        secrets.Count.ShouldBe( 3 );
        secrets["duende:clientsecret"].ShouldBe( "secret" );
        secrets["Db"].ShouldBe( "Server=.;Database=X;Integrated Security=True" );
        secrets["Port:Number"].ShouldBe( "3712" );

        ConfigurationSecret.TryReadFile( root.AppendPart( "Missing.json" ), out var none, out _ ).ShouldBeTrue();
        none.ShouldBeEmpty();

        File.WriteAllText( file, """{ "A": [ "x" ] }""" );
        ConfigurationSecret.TryReadFile( file, out _, out error ).ShouldBeFalse();
        error.ShouldContain( "a secret is a string, a number or a boolean" );
        File.WriteAllText( file, """{ "Bad Name": "x" }""" );
        ConfigurationSecret.TryReadFile( file, out _, out error ).ShouldBeFalse();
        error.ShouldContain( "Invalid secret name 'Bad Name'" );
    }

    [Test]
    public void the_handles_of_a_fleet_may_use_its_placeholders()
    {
        var app = Load( """
            {
              "App": { "FullName": "Sample/$Back-{Name}", "Instances": [ "Red", "Blue" ], "MaxInstanceCount": 3,
                       "Local": { "Key": { "$SECRET": "Back:{Name}:Key" }, "Other": { "$SECRET": "Back-{Index}" } } }
            }
            """ );
        app.CheckSecrets( TestHelper.Monitor ).ShouldBeTrue();

        var bad = Load( """
            {
              "App": { "FullName": "Sample/$Back-{Name}", "InstanceCount": 2,
                       "Local": { "Key": { "$SECRET": "Back {Name}" } } }
            }
            """ );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            bad.CheckSecrets( TestHelper.Monitor ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "Invalid secret name 'Back 1'" ) && t.Contains( "resolved for an instance" ) );
        }
    }

    [Test]
    public void an_engine_resolves_a_secret_from_the_development_secrets()
    {
        var context = new AppBuilderContext( new EngineConfiguration(), [], null, new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase ) { { "Db", "cs" } } );
        context.TryResolveSecret( TestHelper.Monitor, "db", out var value ).ShouldBeTrue();
        value.ShouldBe( "cs" );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            context.TryResolveSecret( TestHelper.Monitor, "Other", out _ ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "The secret 'Other' is required by the build" ) );
            context.TryResolveSecret( TestHelper.Monitor, "Db:{Name}", out _ ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "uses the placeholders of the instances of a fleet" ) );
        }
    }

    [TestCase( "Duende:ClientSecret", true )]
    [TestCase( "A_b-3", true )]
    [TestCase( "", false )]
    [TestCase( "A:", false )]
    [TestCase( "A.B", false )]
    [TestCase( "A B", false )]
    public void secret_names( string name, bool valid )
    {
        ConfigurationSecret.IsValidName( name ).ShouldBe( valid );
    }
}
