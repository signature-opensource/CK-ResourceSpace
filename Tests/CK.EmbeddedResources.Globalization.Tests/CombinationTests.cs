using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.EmbeddedResources.Globalization.Tests;

[TestFixture]
public class CombinationTests
{
    [Test]
    public void ToInitialFinalSet()
    {
        var p = new CodeGenResourceContainer( "P" );

        p.AddText( "locales/default.jsonc", """{ "Msg": "Hello" }""" );
        p.AddText( "locales/fr.jsonc", """{ "Msg": "Bonjour" }""" );
        p.AddText( "locales/fr-FR.jsonc", """{ "Msg": "Salut" }""" );
        p.AddText( "locales/fr-FR-A1.jsonc", """{ "Msg": "Wesh" }""" );
        p.AddText( "locales/fr-CA.jsonc", """{ "Msg": "Bon matin" }""" );

        p.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var set, "locales" ).ShouldBeTrue();
        set.ShouldNotBeNull();

        var final = set.ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();
        final.FindTranslationSet( C.En ).ShouldNotBeNull().Translations["Msg"].Text.ShouldBe( "Hello" );
        final.FindTranslationSet( C.Fr ).ShouldNotBeNull().Translations["Msg"].Text.ShouldBe( "Bonjour" );
        final.FindTranslationSet( C.FrFR ).ShouldNotBeNull().Translations["Msg"].Text.ShouldBe( "Salut" );
        final.FindTranslationSet( C.FrFRA1 ).ShouldNotBeNull().Translations["Msg"].Text.ShouldBe( "Wesh" );
        final.FindTranslationSet( C.FrCA ).ShouldNotBeNull().Translations["Msg"].Text.ShouldBe( "Bon matin" );

        final.FindTranslationSet( C.FrFRA2 ).ShouldBeNull();
        final.FindTranslationSet( C.Es ).ShouldBeNull();
    }

    [Test]
    public void ToInitialFinalSet_with_regular_Override_is_an_error()
    {
        var p = new CodeGenResourceContainer( "P" );
        p.AddText( "locales/default.jsonc", """{ "O:Regular": "R", "O?:Optional": "O", "O!:Always": "A", }""" );

        p.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var set, "locales" ).ShouldBeTrue();
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            set.ShouldNotBeNull().ToInitialFinalSet( TestHelper.Monitor ).ShouldBeNull();
            logs.ShouldContain( """
                Invalid initial set of translation definitions 'locales/default.jsonc' in P.
                No translation can be defined as regular override ("O:"). Only optional ("O?" - that will be skipped) and always ("O!:" - that will be kept) are allowed.
                The following resources are regular override definitions:
                Regular
                """ );
        }
    }

    [Test]
    public void Aggregate_with_ambiguities_and_resolution()
    {
        var p1 = new CodeGenResourceContainer( "P1" );
        p1.AddText( "locales/default.jsonc", """{ "Msg": "Hello" }""" );
        p1.AddText( "locales/fr.jsonc", """{ "Msg": "Bonjour" }""" );
        p1.AddText( "locales/fr-FR-A1.jsonc", """{ "Msg": "Wesh" }""" );
        p1.AddText( "locales/es.jsonc", """{ "Msg": "Hola" }""" );
        p1.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var def1, "locales" ).ShouldBeTrue();
        var f1 = def1.ShouldNotBeNull().ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();

        var p2 = new CodeGenResourceContainer( "P2" );
        p2.AddText( "locales/default.jsonc", """{ "Msg": "Hello" }""" );
        p2.AddText( "locales/fr-FR-A1.jsonc", """{ "Msg": "Yo" }""" );
        p2.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var def2, "locales" ).ShouldBeTrue();
        var f2 = def2.ShouldNotBeNull().ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();

        var agg = f1.Aggregate( f2 );
        var fr = agg.FindTranslationSet( C.Fr ).ShouldNotBeNull();
        fr.IsAmbiguous.ShouldBeFalse( "Ambiguity is not hierarchical." );
        fr.Children.ShouldBeEmpty( "Translation sets without translations are not instantiated" );

        var frFRA1 = agg.FindTranslationSet( C.FrFRA1 ).ShouldNotBeNull();
        frFRA1.IsAmbiguous.ShouldBeTrue();
        frFRA1.Translations["Msg"].Ambiguities.ShouldNotBeEmpty();

        agg.FindTranslationSet( C.Es ).ShouldNotBeNull( "Spanish has been aggregated." ).IsAmbiguous.ShouldBeFalse();
        agg.IsAmbiguous.ShouldBeTrue( "The root or'ed all the sets ambiguities." );

        // Resolution by the "Override folder": this folder doesn't need to specify "O:" overrides
        // as all its definitions are by design overrides.
        // Moreover, it doesn't require a "default.jsonc" (and if there's one, it can be named "en.json).
        {
            var pOverride = new CodeGenResourceContainer( "POverride" );
            pOverride.AddText( "locales/fr-FR-A1.jsonc", """{ "Msg": "It's Yo!" }""" );
            pOverride.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var defOverride, "locales", isOverrideFolder: true ).ShouldBeTrue();
            var fOverride = defOverride.ShouldNotBeNull().Combine( TestHelper.Monitor, agg ).ShouldNotBeNull();
            fOverride.IsAmbiguous.ShouldBeFalse();
            var frFRA1Fixed = fOverride.FindTranslationSet( C.FrFRA1 ).ShouldNotBeNull();
            frFRA1Fixed.IsAmbiguous.ShouldBeFalse();
            var t = frFRA1Fixed.Translations["Msg"];
            t.Ambiguities.ShouldBeNull();
            t.Text.ShouldBe( "It's Yo!" );
        }

        // Resolution by a regular folder. The 'default.jsonc' must exist, even if it can be empty because
        // overrides are not actual definitions!
        {
            var pOverride = new CodeGenResourceContainer( "SomeP" );
            pOverride.AddText( "locales/default.jsonc", "{}" );
            pOverride.AddText( "locales/fr-FR-A1.jsonc", """{ "O:Msg": "It's Yo!" }""" );
            pOverride.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var defOverride, "locales", isOverrideFolder: false ).ShouldBeTrue();
            var fOverride = defOverride.ShouldNotBeNull().Combine( TestHelper.Monitor, agg ).ShouldNotBeNull();
            fOverride.IsAmbiguous.ShouldBeFalse();
            var frFRA1Fixed = fOverride.FindTranslationSet( C.FrFRA1 ).ShouldNotBeNull();
            frFRA1Fixed.IsAmbiguous.ShouldBeFalse();
            var t = frFRA1Fixed.Translations["Msg"];
            t.Ambiguities.ShouldBeNull();
            t.Text.ShouldBe( "It's Yo!" );
        }
    }

    [Test]
    public void Aggregate_function_with_empty_translations()
    {
        var p1 = CreateEnFr( "p1", """{ "K1": "en-P1" }""", """{ "K1": "fr-P1" }""" );
        var p2 = CreateEnFr( "p2", """{ "K2": "en-P2" }""", """{                }""" );
        var p3 = CreateEnFr( "p3", """{ "K3": "en-P3" }""", """{ "K3": "fr-P3" }""" );
        var p4 = CreateEnFr( "p4", """{ "K4": "en-P4" }""", """{                }""" );
        var p5 = CreateEnFr( "p5", """{ "K5": "en-P5" }""", """{ "K5": "fr-P5" }""" );
        var p6 = CreateEnFr( "p6", """{               }""", """{                }""" );
        var p7 = CreateEnFr( "p7", """{ "K7": "en-P7" }""", """{ "K7": "fr-P7" }""" );

        var f1 = p1.ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();
        var f2 = p2.ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();
        var f3 = p3.ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();
        var f4 = p4.ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();
        var f5 = p5.ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();
        var f6 = p6.ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();
        var f7 = p7.ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();

        var fLeft = f1.Aggregate( f2 ).Aggregate( f3 ).Aggregate( f4 ).Aggregate( f5 ).Aggregate( f6 ).Aggregate( f7 );
        fLeft.IsAmbiguous.ShouldBeFalse();
        fLeft.Translations.Keys.ShouldBe( ["K1", "K2", "K3", "K4", "K5", "K7",], ignoreOrder: true );
        fLeft.Children.Single().Translations.Keys.ShouldBe( ["K1", "K3", "K5", "K7"], ignoreOrder: true );

        var fRight = f7.Aggregate( f6 ).Aggregate( f5 ).Aggregate( f4 ).Aggregate( f3 ).Aggregate( f2 ).Aggregate( f1 );
        fRight.IsAmbiguous.ShouldBeFalse();
        fRight.Translations.Keys.ShouldBe( ["K1", "K2", "K3", "K4", "K5", "K7",], ignoreOrder: true );
        fRight.Children.Single().Translations.Keys.ShouldBe( ["K1", "K3", "K5", "K7",], ignoreOrder: true );

    }

    [Test]
    public void Aggregate_function_without_ambiguities()
    {
        var p1 = CreateEnFr( "p1", """{ "K1": "Hop", "K2": "Hip" , "K3": "Bing" }""", """{ "K1": "HOP" }""" );
        var p2 = CreateEnFr( "p2", """{ "K1": "Hop", "K2": "Hip" }""", """{ "K1": "HOP", "K2": "HIP" }""" );

        var f1 = p1.ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();
        var f2 = p2.ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();

        Check( f1.Aggregate( f2 ) );
        Check( f2.Aggregate( f1 ) );
    }

    private static void Check( FinalTranslationSet agg )
    {
        agg.IsAmbiguous.ShouldBeFalse();
        agg.Translations.Keys.ShouldBe( ["K1", "K2", "K3"], ignoreOrder: true );
        agg.Translations["K1"].Text.ShouldBe( "Hop" );
        agg.Translations["K2"].Text.ShouldBe( "Hip" );
        agg.Translations["K3"].Text.ShouldBe( "Bing" );
        var fr = agg.Children.Single().Translations;
        fr.Keys.ShouldBe( ["K1", "K2"], ignoreOrder: true );
        fr["K1"].Text.ShouldBe( "HOP" );
        fr["K2"].Text.ShouldBe( "HIP" );
    }

    [Test]
    public void Combine_function()
    {
        var p1 = CreateEnFr( "p1", """{ "K1": "en-P1" }""", """{ "K1": "fr-P1" }""" );
        var p2 = CreateEnFr( "p2", """{ "K2": "en-P2" }""", """{                }""" );
        var p3 = CreateEnFr( "p3", """{ "K3": "en-P3" }""", """{ "K3": "fr-P3" }""" );
        var p4 = CreateEnFr( "p4", """{ "K4": "en-P4" }""", """{                }""" );
        var p5 = CreateEnFr( "p5", """{ "K5": "en-P5" }""", """{ "K5": "fr-P5" }""" );
        var p6 = CreateEnFr( "p6", """{               }""", """{                }""" );
        var p7 = CreateEnFr( "p7", """{ "K7": "en-P7" }""", """{ "K7": "fr-P7" }""" );

        {
            var f1 = p1.ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();
            var f2 = p2.Combine( TestHelper.Monitor, f1 ).ShouldNotBeNull();
            var f3 = p3.Combine( TestHelper.Monitor, f2 ).ShouldNotBeNull();
            var f4 = p4.Combine( TestHelper.Monitor, f3 ).ShouldNotBeNull();
            var f5 = p5.Combine( TestHelper.Monitor, f4 ).ShouldNotBeNull();
            var f6 = p6.Combine( TestHelper.Monitor, f5 ).ShouldNotBeNull();
            var f7 = p7.Combine( TestHelper.Monitor, f6 ).ShouldNotBeNull();

            f7.IsAmbiguous.ShouldBeFalse();
            f7.Translations.Keys.ShouldBe( ["K1", "K2", "K3", "K4", "K5", "K7",], ignoreOrder: true );
            f7.Children.Single().Translations.Keys.ShouldBe( ["K1", "K3", "K5", "K7"], ignoreOrder: true );
        }

        {
            var f7 = p7.ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();
            var f6 = p6.Combine( TestHelper.Monitor, f7 ).ShouldNotBeNull();
            var f5 = p5.Combine( TestHelper.Monitor, f6 ).ShouldNotBeNull();
            var f4 = p4.Combine( TestHelper.Monitor, f5 ).ShouldNotBeNull();
            var f3 = p3.Combine( TestHelper.Monitor, f4 ).ShouldNotBeNull();
            var f2 = p2.Combine( TestHelper.Monitor, f3 ).ShouldNotBeNull();
            var f1 = p1.Combine( TestHelper.Monitor, f2 ).ShouldNotBeNull();

            f1.IsAmbiguous.ShouldBeFalse();
            f1.Translations.Keys.ShouldBe( ["K1", "K2", "K3", "K4", "K5", "K7",], ignoreOrder: true );
            f1.Children.Single().Translations.Keys.ShouldBe( ["K1", "K3", "K5", "K7"], ignoreOrder: true );
        }


    }

    TranslationDefinitionSet CreateEnFr( string name, string v1, string v2 )
    {
        var c = new CodeGenResourceContainer( name );
        c.AddText( "locales/default.jsonc", v1 );
        c.AddText( "locales/fr.jsonc", v2 );
        c.LoadTranslations( TestHelper.Monitor, C.EnFrSet, out var translations, "locales" ).ShouldBeTrue();
        translations.ShouldNotBeNull();
        return translations;
    }

    [Test]
    public void Override_in_specific_culture_doesnt_require_root_definition()
    {
        var zorro = new CodeGenResourceContainer( "Zorro" );
        zorro.AddText( "locales/default.jsonc", """{ "Button.Confirm": "Confirm" }""" );
        zorro.AddText( "locales/fr.jsonc", """{ "Button.Confirm": "Confirmer" }""" );
        zorro.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var zorroDefinition, "locales" ).ShouldBeTrue();
        var zorroSet = zorroDefinition.ShouldNotBeNull().ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();

        // Without Component:
        {
            zorroSet.FindTranslationSet( C.FrFR ).ShouldBeNull();
            var s = zorroSet.FindTranslationSetOrParent( C.FrFR ).ShouldNotBeNull();
            s.Translations["Button.Confirm"].Text.ShouldBe( "Confirmer" );
        }

        var component = new CodeGenResourceContainer( "Component" );
        component.AddText( "locales/default.jsonc", """{ }""" );
        component.AddText( "locales/fr-fr.jsonc", """{ "O:Button.Confirm": "Confirmer !" }""" );
        component.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var componentDefinition, "locales" ).ShouldBeTrue();
        var componentSet = componentDefinition.ShouldNotBeNull().Combine( TestHelper.Monitor, zorroSet ).ShouldNotBeNull();

        // With Component:
        {
            var s = componentSet.FindTranslationSet( C.FrFR ).ShouldNotBeNull();
            s.Translations["Button.Confirm"].Text.ShouldBe( "Confirmer !" );
            s.ClosestParent.ShouldNotBeNull().Culture.Culture.Name.ShouldBe( "fr" );
            s.ClosestParent.Translations["Button.Confirm"].Text.ShouldBe( "Confirmer" );
        }
    }
}
