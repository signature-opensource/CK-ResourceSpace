using CK.Core;
using NUnit.Framework;
using Shouldly;
using static CK.Testing.MonitorTestHelper;

namespace CK.EmbeddedResources.Globalization.Tests;

[TestFixture]
public class LoadTranslationsTests
{
    [Test]
    public void a_regular_folder_without_default_jsonc_is_an_error()
    {
        var p = new CodeGenResourceContainer( "P" );
        p.AddText( "locales/fr-FR.jsonc", """{ "Msg": "Bonjour" }""" );
        p.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var translations, "locales" ).ShouldBeFalse();
        translations.ShouldBeNull();
    }

    [Test]
    public void an_invalid_default_jsonc_is_an_error()
    {
        var p = new CodeGenResourceContainer( "P" );
        p.AddText( "locales/default.jsonc", """{ "Msg": """ );
        p.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var translations, "locales" ).ShouldBeFalse();
        translations.ShouldBeNull();
    }

    [Test]
    public void no_folder_and_an_override_folder_without_default_are_not_errors()
    {
        var noFolder = new CodeGenResourceContainer( "NoFolder" );
        noFolder.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var none, "locales" ).ShouldBeTrue();
        none.ShouldBeNull();

        var overrides = new CodeGenResourceContainer( "Overrides" );
        overrides.AddText( "locales/fr-FR.jsonc", """{ "O:Msg": "Bonjour" }""" );
        overrides.LoadTranslations( TestHelper.Monitor, C.ActiveCultures, out var o, "locales", isOverrideFolder: true ).ShouldBeTrue();
        o.ShouldNotBeNull();
    }
}
