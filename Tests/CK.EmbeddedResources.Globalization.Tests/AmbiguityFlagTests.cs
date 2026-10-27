using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.EmbeddedResources.Globalization.Tests;

/// <summary>
/// The root <see cref="FinalTranslationSet.IsAmbiguous"/> must be true if and only if an ambiguity exists
/// in the root or in any sub set, whatever the sequence of Aggregate and Combine that produced it.
/// </summary>
[TestFixture]
public class AmbiguityFlagTests
{
    static TranslationDefinitionSet Definitions( string name, params (string File, string Content)[] files )
    {
        var p = new CodeGenResourceContainer( name );
        foreach( var (f, c) in files ) p.AddText( "locales/" + f, c );
        p.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var d, "locales" ).ShouldBeTrue();
        return d.ShouldNotBeNull();
    }

    static FinalTranslationSet Final( string name, params (string File, string Content)[] files )
    {
        return Definitions( name, files ).ToInitialFinalSet( TestHelper.Monitor ).ShouldNotBeNull();
    }

    static void ShouldBeCoherent( FinalTranslationSet s, bool expected )
    {
        s.IsAmbiguous.ShouldBe( expected );
        s.Ambiguities.Any().ShouldBe( expected );
    }

    /// <summary>
    /// Two packages with conflicting "fr" texts: the ambiguity is only in the "fr" sub set.
    /// </summary>
    static FinalTranslationSet FrAmbiguous()
    {
        var a1 = Final( "A1", ("default.jsonc", """{ "K1": "x", "Msg": "Hello", "K2": "y" }"""), ("fr.jsonc", """{ "Msg": "A", "K2": "k" }""") );
        var a2 = Final( "A2", ("default.jsonc", """{ "K1": "x", "Msg": "Hello", "K2": "y" }"""), ("fr.jsonc", """{ "Msg": "B", "K2": "k" }""") );
        var amb = a1.Aggregate( a2 );
        ShouldBeCoherent( amb, true );
        return amb;
    }

    [Test]
    public void Aggregate_keeps_the_ambiguity_of_a_sub_set_that_the_other_set_does_not_have()
    {
        var bigger = Final( "P3", ("default.jsonc", """{ "K1": "x", "Msg": "Hello", "K2": "y", "K3": "z" }""") );
        ShouldBeCoherent( FrAmbiguous().Aggregate( bigger ), true );
        ShouldBeCoherent( bigger.Aggregate( FrAmbiguous() ), true );
    }

    [Test]
    public void Combine_keeps_the_ambiguity_of_an_unchanged_sub_set()
    {
        var d = Definitions( "D", ("default.jsonc", """{ "New": "n" }"""), ("fr.jsonc", "{}") );
        ShouldBeCoherent( d.Combine( TestHelper.Monitor, FrAmbiguous() ).ShouldNotBeNull(), true );
    }

    [Test]
    public void Combine_keeps_a_root_ambiguity_when_another_key_is_overridden()
    {
        var r1 = Final( "R1", ("default.jsonc", """{ "Msg": "A", "K": "k" }""") );
        var r2 = Final( "R2", ("default.jsonc", """{ "Msg": "B", "K": "k" }""") );
        var rootAmbiguous = r1.Aggregate( r2 );
        ShouldBeCoherent( rootAmbiguous, true );

        var o = Definitions( "O", ("default.jsonc", """{ "O:K": "k2" }""") );
        ShouldBeCoherent( o.Combine( TestHelper.Monitor, rootAmbiguous ).ShouldNotBeNull(), true );
    }

    [Test]
    public void Combine_clears_the_flag_when_an_override_resolves_the_ambiguity_even_if_another_key_is_overridden_after()
    {
        var o = Definitions( "O", ("default.jsonc", "{}"), ("fr.jsonc", """{ "O:Msg": "C", "O:K2": "k3" }""") );
        ShouldBeCoherent( o.Combine( TestHelper.Monitor, FrAmbiguous() ).ShouldNotBeNull(), false );
    }
}
