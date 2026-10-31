using CK.Core;
using NUnit.Framework;
using Shouldly;
using static CK.Testing.MonitorTestHelper;

namespace CK.ResourceSpace.Tests;

/// <summary>
/// The resources of the optional packages that are eventually not kept are the ResCoreData.ExcludedOptionalResourcePaths:
/// a transformer that targets one of them is ignored.
/// </summary>
[TestFixture]
public class ExcludedOptionalResourcePathsTests
{
    [TestCase( false )]
    [TestCase( true )]
    public void the_resources_of_the_dropped_optional_packages_are_excluded( bool withPromotedOne )
    {
        var config = new ResSpaceConfiguration();
        var root = config.RegisterPackage( TestHelper.Monitor, typeof( Closure.Unrelated ), isOptional: false ).ShouldNotBeNull();
        config.RegisterPackage( TestHelper.Monitor, typeof( P1.Package ), isOptional: true ).ShouldNotBeNull();
        if( withPromotedOne )
        {
            // P2 is optional but required: it is kept.
            var p2 = config.RegisterPackage( TestHelper.Monitor, typeof( P2.Package ), isOptional: true ).ShouldNotBeNull();
            root.Requires.Add( p2 );
        }
        var data = new ResSpaceDataBuilder( config.Build( TestHelper.Monitor ).ShouldNotBeNull() ).Build( TestHelper.Monitor ).ShouldNotBeNull().CoreData;
        // Before the fix, when all the optional packages were dropped, the list of the dropped ones was cleared.
        data.ExcludedOptionalResourcePaths.ShouldContain( p => p.EndsWith( "logo.png" ) );
        data.ExcludedOptionalResourcePaths.Length.ShouldBe( 2 );
    }

    [Test]
    public void no_resources_are_excluded_when_the_optional_packages_are_kept()
    {
        var config = new ResSpaceConfiguration();
        var root = config.RegisterPackage( TestHelper.Monitor, typeof( Closure.Unrelated ), isOptional: false ).ShouldNotBeNull();
        var p1 = config.RegisterPackage( TestHelper.Monitor, typeof( P1.Package ), isOptional: true ).ShouldNotBeNull();
        root.Requires.Add( p1 );
        var data = new ResSpaceDataBuilder( config.Build( TestHelper.Monitor ).ShouldNotBeNull() ).Build( TestHelper.Monitor ).ShouldNotBeNull().CoreData;
        data.ExcludedOptionalResourcePaths.ShouldBeEmpty();
    }
}
