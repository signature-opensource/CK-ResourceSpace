using CK.Core;
using CK.Setup;
using NUnit.Framework;
using Shouldly;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.Engine.Configuration.Tests;

[TestFixture]
public class TypeSetConfigurationTests
{
    public class SomeType { }

    [Test]
    public void the_features_are_in_the_xml_and_case_insensitive()
    {
        var c = new EngineConfiguration();
        var s = new TypeSetConfiguration( "$Party" );
        s.Features.Add( "HttpEndpoint" );
        s.ExcludedFeatures.Add( "Cris.Sender" );
        c.FirstBinPath.AddTypeSet( s );
        var xml = c.ToXml();
        var back = new EngineConfiguration( xml ).FirstBinPath.FindTypeSet( "$Party" ).ShouldNotBeNull();
        back.Features.ShouldContain( "httpendpoint" );
        back.ExcludedFeatures.ShouldBe( ["Cris.Sender"] );
        back.SetEquals( s ).ShouldBeTrue();
        back.GetContentHashCode().ShouldBe( s.GetContentHashCode() );
        XElement.DeepEquals( new EngineConfiguration( xml ).ToXml(), xml ).ShouldBeTrue();
    }

    [TestCase( "HttpEndpoint", true )]
    [TestCase( "Cris.Sender2", true )]
    [TestCase( "2Cris", false )]
    [TestCase( "Cris.", false )]
    [TestCase( "Cris..Sender", false )]
    [TestCase( "Cris-Sender", false )]
    [TestCase( "", false )]
    public void feature_names_are_identifiers( string name, bool valid )
    {
        TypeSetConfiguration.IsValidFeatureName( name ).ShouldBe( valid );
    }

    [Test]
    public void a_set_cannot_add_and_exclude_a_feature_nor_use_an_invalid_name()
    {
        var c = new EngineConfiguration();
        var s = new TypeSetConfiguration( "$Party" );
        s.Features.Add( "A" );
        s.ExcludedFeatures.Add( "a" );
        s.Features.Add( "Not-Valid" );
        c.FirstBinPath.AddTypeSet( s );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            c.NormalizeConfiguration( TestHelper.Monitor ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "both adds and excludes the feature 'A'" ) );
            texts.ShouldContain( t => t.Contains( "'Not-Valid' is not a valid feature name" ) );
        }
    }

    [Test]
    public void a_set_cannot_add_what_an_ancestor_excludes()
    {
        var c = new EngineConfiguration();
        var party = new TypeSetConfiguration( "$Party" );
        party.ExcludedTypes.Add( typeof( SomeType ) );
        party.ExcludedFeatures.Add( "Hosting" );
        var client = new TypeSetConfiguration( "App" ) { Parent = "$Party" };
        var sub = new TypeSetConfiguration( "Sub" ) { Parent = "App" };
        sub.Types.Add( typeof( SomeType ) );
        sub.Features.Add( "hosting" );
        c.FirstBinPath.AddTypeSet( party );
        c.FirstBinPath.AddTypeSet( client );
        c.FirstBinPath.AddTypeSet( sub );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            c.NormalizeConfiguration( TestHelper.Monitor ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "TypeSet 'Sub'" ) && t.Contains( "that its ancestor TypeSet '$Party' excludes" ) && t.Contains( "SomeType" ) );
            texts.ShouldContain( t => t.Contains( "adds the feature 'hosting' that its ancestor TypeSet '$Party' excludes" ) );
        }
    }

    [Test]
    public void the_parents_cannot_form_a_cycle()
    {
        var c = new EngineConfiguration();
        c.FirstBinPath.AddTypeSet( new TypeSetConfiguration( "A" ) { Parent = "B" } );
        c.FirstBinPath.AddTypeSet( new TypeSetConfiguration( "B" ) { Parent = "A" } );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            c.NormalizeConfiguration( TestHelper.Monitor ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "form a cycle" ) );
        }
    }
}
