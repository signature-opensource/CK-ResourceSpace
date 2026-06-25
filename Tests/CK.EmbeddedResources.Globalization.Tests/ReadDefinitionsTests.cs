using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.EmbeddedResources.Globalization.Tests;


[TestFixture]
public class ReadDefinitionsTests
{
    [Test]
    public void basic_reads()
    {
        var p = new CodeGenResourceContainer( "P" );

        p.AddText( "locales/default.jsonc", """{ "Msg": "Hello" }""" );
        p.AddText( "locales/fr.jsonc", """{ "Msg": "Bonjour" }""" );
        p.AddText( "locales/fr-FR.jsonc", """{ "Msg": "Salut" }""" );
        p.AddText( "locales/fr-CA.jsonc", """{ "Msg": "Bon matin" }""" );

        p.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var set, "locales" ).ShouldBeTrue();
        set.ShouldNotBeNull();
        set.Culture.Culture.ShouldBe( C.En );
        set.Children.Count().ShouldBe( 1 );
        set.Origin.Container.ShouldBe( p );
        set.Origin.FullResourceName.ShouldBe( "locales/default.jsonc" );

        set.Translations.Count.ShouldBe( 1 );
        set.Translations["Msg"].Override.ShouldBe( ResourceOverrideKind.None );
        set.Translations["Msg"].Text.ShouldBe( "Hello" );

        var fr = set.Children.Single( s => s.Culture.Culture == C.Fr );
        fr.Origin.Container.ShouldBe( p );
        fr.Origin.FullResourceName.ShouldBe( "locales/fr.jsonc" );

        fr.Translations.Count.ShouldBe( 1 );
        fr.Translations["Msg"].Override.ShouldBe( ResourceOverrideKind.None );
        fr.Translations["Msg"].Text.ShouldBe( "Bonjour" );

        var frFR = fr.Children.Single( s => s.Culture.Culture == C.FrFR );
        frFR.Origin.Container.ShouldBe( p );
        frFR.Origin.FullResourceName.ShouldBe( "locales/fr-FR.jsonc" );
        frFR.Translations.Count.ShouldBe( 1 );
        frFR.Translations["Msg"].Override.ShouldBe( ResourceOverrideKind.None );
        frFR.Translations["Msg"].Text.ShouldBe( "Salut" );

        var frCA = fr.Children.Single( s => s.Culture.Culture == C.FrCA );
        frCA.Origin.Container.ShouldBe( p );
        frCA.Origin.FullResourceName.ShouldBe( "locales/fr-CA.jsonc" );
        frCA.Translations.Count.ShouldBe( 1 );
        frCA.Translations["Msg"].Override.ShouldBe( ResourceOverrideKind.None );
        frCA.Translations["Msg"].Text.ShouldBe( "Bon matin" );
    }

    [Test]
    public void sub_object_reads()
    {
        var p = new CodeGenResourceContainer( "P" );

        p.AddText( "locales/default.jsonc", """
            {
                "Msg": "Hello",
                "Top": {
                    "Messages": {
                        "One": "1",
                        "Two": "2"
                    }
                }
            }
            """ );

        p.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var set, "locales" ).ShouldBeTrue();
        set.ShouldNotBeNull();
        set.Translations["Msg"].Text.ShouldBe( "Hello" );
        set.Translations["Top.Messages.One"].Text.ShouldBe( "1" );
        set.Translations["Top.Messages.Two"].Text.ShouldBe( "2" );
    }
}
