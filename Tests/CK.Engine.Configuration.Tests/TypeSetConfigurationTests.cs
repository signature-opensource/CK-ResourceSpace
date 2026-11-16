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
    public void the_types_are_in_the_xml()
    {
        var c = new EngineConfiguration();
        var s = new TypeSetConfiguration( "$Party" ) { Parent = "Root" };
        s.Types.Add( typeof( SomeType ) );
        s.ExcludedTypes.Add( typeof( SpecializedType ) );
        c.FirstBinPath.AddTypeSet( new TypeSetConfiguration( "Root" ) );
        c.FirstBinPath.AddTypeSet( s );
        var xml = c.ToXml();
        var back = new EngineConfiguration( xml ).FirstBinPath.FindTypeSet( "$Party" ).ShouldNotBeNull();
        back.Types.ShouldBe( [typeof( SomeType )] );
        back.ExcludedTypes.ShouldBe( [typeof( SpecializedType )] );
        back.SetEquals( s ).ShouldBeTrue();
        back.GetContentHashCode().ShouldBe( s.GetContentHashCode() );
        XElement.DeepEquals( new EngineConfiguration( xml ).ToXml(), xml ).ShouldBeTrue();
    }

    [Test]
    public void a_set_cannot_add_what_an_ancestor_excludes()
    {
        var c = new EngineConfiguration();
        var party = new TypeSetConfiguration( "$Party" );
        party.ExcludedTypes.Add( typeof( SomeType ) );
        var client = new TypeSetConfiguration( "App" ) { Parent = "$Party" };
        var sub = new TypeSetConfiguration( "Sub" ) { Parent = "App" };
        sub.Types.Add( typeof( SomeType ) );
        c.FirstBinPath.AddTypeSet( party );
        c.FirstBinPath.AddTypeSet( client );
        c.FirstBinPath.AddTypeSet( sub );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            c.NormalizeConfiguration( TestHelper.Monitor ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "TypeSet 'Sub'" ) && t.Contains( "that its ancestor TypeSet '$Party' excludes" ) && t.Contains( "SomeType" ) );
        }
    }

    public class SpecializedType : SomeType { }

    [Test]
    public void an_exclusion_excludes_the_specializations()
    {
        var c = new EngineConfiguration();
        c.FirstBinPath.ExcludedTypes.Add( typeof( SomeType ) );
        c.FirstBinPath.Types.Add( typeof( SpecializedType ) );
        var s = new TypeSetConfiguration( "$Party" );
        s.Types.Add( typeof( SpecializedType ) );
        c.FirstBinPath.AddTypeSet( s );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            c.NormalizeConfiguration( TestHelper.Monitor ).ShouldBeFalse();
            texts.ShouldContain( t => t.Contains( "SpecializedType" ) && t.Contains( "that is excluded (by" ) && t.Contains( "It is removed and will be ignored." ) );
            texts.ShouldContain( t => t.Contains( "adds the type" ) && t.Contains( "that the BinPath excludes (by" ) );
        }
        c.FirstBinPath.Types.ShouldBeEmpty();
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
