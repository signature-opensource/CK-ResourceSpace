using CK.Core;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.Engine.TypeCollector.Tests;

[TestFixture]
public class EngineAttributeTests
{
    [AttributeMustSuffixTheName]
    public class Failed
    {
    }

    [CorrectlyNamedButBadParent]
    public class FailedIndirect
    {
    }

    [Test]
    public void Attribute_type_name_must_be_suffixed_with_Attribute()
    {
        var c = new GlobalTypeCache();
        var p = c.Get( typeof( Failed ) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            p.TryGetAllAttributes( TestHelper.Monitor, out _ ).ShouldBeFalse();
            logs.ShouldContain( """
                    Attribute name 'CK.Engine.TypeCollector.Tests.AttributeMustSuffixTheName' is invalid: it must be suffixed by "Attribute".
                    """ );
        }
        var i = c.Get( typeof( FailedIndirect ) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            i.TryGetAllAttributes( TestHelper.Monitor, out _ ).ShouldBeFalse();
            logs.ShouldContain( """
                    Attribute name 'CK.Engine.TypeCollector.Tests.AttributeMustSuffixTheName' is invalid: it must be suffixed by "Attribute".
                    """ );
        }
        // The error is cached.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            p.TryGetAllAttributes( TestHelper.Monitor, out _ ).ShouldBeFalse();
            i.TryGetAllAttributes( TestHelper.Monitor, out _ ).ShouldBeFalse();
            logs.ShouldNotContain( "Attribute name 'CK.Engine.TypeCollector.Tests.AttributeMustSuffixTheName' is invalid: it must be suffixed by \"Attribute\"." );
        }
    }

    [SomeEngine( "Demo" )]
    [SomeChildEngine( "Demo-1" )]
    [SomeChildEngine( "Demo-2" )]
    [OneEngine( "AnotherDemo" )]
    [OneChildEngine( "AnotherDemo-1" )]
    [OneChildEngine( "AnotherDemo-2" )]
    [CanBeBuggy( "DemoUnrelated" )]
    public interface SomeInterface
    {
        [SomeEngine( "Demo" )]
        [SomeChildEngine( "Demo-1" )]
        [SomeChildEngine( "Demo-2" )]
        [SomeChildEngine( "Demo-3" )]
        string DoSomething( int value );
    }


    [Test]
    public void TryGetAllAttributes_on_type()
    {
        var c = new GlobalTypeCache();
        var t = c.Get( typeof( SomeInterface ) );
        t.TryGetAllAttributes( TestHelper.Monitor, out var attributes ).ShouldBeTrue();
        attributes.Length.ShouldBe( 8 );
        attributes[0].ShouldBeAssignableTo<System.Runtime.CompilerServices.NullableContextAttribute>();
        attributes.Skip( 1 )
                  .Select( a => a.GetType().Name )
                  .ShouldBe( ["SomeEngineAttributeImpl",
                              "SomeChildEngineAttributeImpl",
                              "SomeChildEngineAttributeImpl",
                              "OneEngineAttributeImpl",
                              "OneChildEngineAttributeImpl",
                              "OneChildEngineAttributeImpl",
                              "CanBeBuggyAttributeImpl"] );

        // Attribute declarations ordering is preserved.
        attributes.OfType<IAttributeHasNameProperty>().Select( a => a.TheAttributeName )
          .ShouldBe( ["Demo",
                      "Demo-1",
                      "Demo-2",
                      "AnotherDemo",
                      "AnotherDemo-1",
                      "AnotherDemo-2",
                      "DemoUnrelated"
                   ] );
        var withTypedChildren = attributes.OfType<OneEngineAttributeImpl>().Single();
        withTypedChildren.ChildrenImpl.Count().ShouldBe( 2 );
        withTypedChildren.ChildrenImpl.ShouldAllBe( a => a.TheAttributeName.StartsWith( "AnotherDemo-" ) );
    }

    [Test]
    public void TryGetInitializedAttributes_on_member()
    {
        var c = new GlobalTypeCache();
        var t = c.Get( typeof( SomeInterface ) );
        var method = t.DeclaredMembers.Single( m => m.Name == "DoSomething" );
        method.TryGetAllAttributes( TestHelper.Monitor, out var attributes ).ShouldBeTrue();
        attributes.Length.ShouldBe( 4 );
        attributes.Select( a => a.GetType().Name )
                  .ShouldBe( ["SomeEngineAttributeImpl",
                              "SomeChildEngineAttributeImpl",
                              "SomeChildEngineAttributeImpl",
                              "SomeChildEngineAttributeImpl"] );

        // Attribute declarations ordering is preserved.
        attributes.OfType<IAttributeHasNameProperty>().Select( a => a.TheAttributeName )
          .ShouldBe( ["Demo",
                      "Demo-1",
                      "Demo-2",
                      "Demo-3"
                   ] );

    }


    [CanBeBuggy( "Buggy" )]
    public interface IBuggy
    {
    }


    const string BuggyOnIntializedMessage = "While calling 'CK.Engine.TypeCollector.Tests.CanBeBuggyAttributeImpl.OnInitialized' from 'CK.Engine.TypeCollector.Tests.EngineAttributeTests.IBuggy'.";

    [Test]
    public void OnInitialized_exceptions_are_caught()
    {
        CanBeBuggyAttribute.ImplOnInitializedThrow = true;
        try
        {
            var c = new GlobalTypeCache();
            var t = c.Get( typeof( IBuggy ) );
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                t.TryGetAllAttributes( TestHelper.Monitor, out var attributes )
                    .ShouldBeFalse();
                logs.ShouldContain( BuggyOnIntializedMessage );
            }
        }
        finally
        {
            CanBeBuggyAttribute.Reset();
        }
    }

    [OneChildEngine( "Demo-1" )]
    [OneChildEngine( "Demo-2" )]
    public interface IMissingPrimary1
    {
    }

    [Test]
    public void missing_parent_error_details_1()
    {
        var c = new GlobalTypeCache();
        var t = c.Get( typeof( IMissingPrimary1 ) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            t.TryGetAllAttributes( TestHelper.Monitor, out var attributes )
                .ShouldBeFalse();

            logs.ShouldContain( """
                **[OneChildEngine]** Missing a [OneEngine] attribute (or a specialization) above this one.
                **[OneChildEngine]** (Same as above.)
                CK.Engine.TypeCollector.Tests.EngineAttributeTests.IMissingPrimary1
                """ );
        }
    }

    [OneChildEngine( "Demo-1" )]
    [OneChildEngine( "Demo-2" )]
    [SomeChildEngine( "Demo-2" )]
    public interface IMissingPrimary2
    {
    }

    [Test]
    public void missing_parent_error_details_2()
    {
        var c = new GlobalTypeCache();
        var t = c.Get( typeof( IMissingPrimary2 ) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            t.TryGetAllAttributes( TestHelper.Monitor, out var attributes )
                .ShouldBeFalse();

            logs.ShouldContain( """
                **[OneChildEngine]** Missing a [OneEngine] attribute (or a specialization) above this one.
                **[OneChildEngine]** (Same as above.)
                **[SomeChildEngine]** Missing a [SomeEngine] attribute (or a specialization) above this one.
                CK.Engine.TypeCollector.Tests.EngineAttributeTests.IMissingPrimary2
                """ );
        }
    }

    [SomeEngine( "Demo" )]
    [SomeChildEngine( "Demo-1" )]
    [SomeChildEngine( "Demo-2" )]
    [OneChildEngine( "Demo-2" )]
    public interface IMissingPrimary3
    {
    }

    [Test]
    public void missing_parent_error_details_3()
    {
        var c = new GlobalTypeCache();
        var t = c.Get( typeof( IMissingPrimary3 ) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            t.TryGetAllAttributes( TestHelper.Monitor, out var attributes )
                .ShouldBeFalse();

            logs.ShouldContain( """
                [SomeEngine] (n°1)
                [SomeChildEngine] (n°2)
                [SomeChildEngine] (n°3)
                **[OneChildEngine]** Missing a [OneEngine] attribute (or a specialization) above this one.
                CK.Engine.TypeCollector.Tests.EngineAttributeTests.IMissingPrimary3
                """ );
        }
    }

    [OneChildEngine( "Demo-1" )]
    [OneChildEngine( "Demo-2" )]
    [SomeEngine( "Demo" )]
    [OneChildEngine( "AnotherDemo-1" )]
    [OneEngine( "AnotherDemo" )]
    public interface IBadOrder1
    {
    }

    [Test]
    public void bad_order_attributes_error_details_1()
    {
        var c = new GlobalTypeCache();
        var t = c.Get( typeof( IBadOrder1 ) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            t.TryGetAllAttributes( TestHelper.Monitor, out var attributes )
                .ShouldBeFalse();

            logs.ShouldContain( """
                **[OneChildEngine]** Must be after [OneEngine] (n°5).
                **[OneChildEngine]** (Same as above.)
                [SomeEngine] (n°3)
                **[OneChildEngine]** Must be after [OneEngine] (n°5).
                [OneEngine] (n°5)
                CK.Engine.TypeCollector.Tests.EngineAttributeTests.IBadOrder1
                """ );
        }
    }

    [SomeEngine( "Demo" )]
    [SomeChildEngine( "Demo-1" )]
    [OneChildEngine( "Demo-2" )]
    [OneChildEngine( "AnotherDemo-1" )]
    [OneEngine( "AnotherDemo" )]
    public interface IBadOrder2
    {
    }

    [Test]
    public void bad_order_attributes_error_details_2()
    {
        var c = new GlobalTypeCache();
        var t = c.Get( typeof( IBadOrder2 ) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            t.TryGetAllAttributes( TestHelper.Monitor, out var attributes )
                .ShouldBeFalse();

            logs.ShouldContain( """
                [SomeEngine] (n°1)
                [SomeChildEngine] (n°2)
                **[OneChildEngine]** Must be after [OneEngine] (n°5).
                **[OneChildEngine]** (Same as above.)
                [OneEngine] (n°5)
                CK.Engine.TypeCollector.Tests.EngineAttributeTests.IBadOrder2
                """ );
        }
    }

    [OneChildEngine( "Bad ordering 1." )]
    [OneChildEngine( "Bad ordering 2." )]
    [OneEngine( "Possible place n°1" )]
    [OneChildEngine( "In place." )]
    [OneEngine( "Possible place n°2" )]
    public interface IBadOrder3
    {
    }

    [Test]
    public void bad_order_attributes_error_details_3()
    {
        var c = new GlobalTypeCache();
        var t = c.Get( typeof( IBadOrder3 ) );
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            t.TryGetAllAttributes( TestHelper.Monitor, out var attributes )
                .ShouldBeFalse();

            logs.ShouldContain( """
                **[OneChildEngine]** Must be after [OneEngine] (n°3) or [OneEngine] (n°5).
                **[OneChildEngine]** (Same as above.)
                [OneEngine] (n°3)
                [OneChildEngine] (n°4)
                [OneEngine] (n°5)
                CK.Engine.TypeCollector.Tests.EngineAttributeTests.IBadOrder3
                """ );
        }
    }

    public class BindingDemo
    {
        [SomeEngineSpec( "Demo", SomeEngineSpecAttribute.ImplBinding.SameAsBase, 3712 )]
        [SomeChildEngine( "Demo-1" )]
        public void UsingBaseImpl() { }

        [SomeEngineSpec( "Demo", SomeEngineSpecAttribute.ImplBinding.SpecializedImpl, 3712 )]
        [SomeChildEngine( "Demo-1" )]
        public void UsingSpecializedImpl() { }

        [SomeEngineSpec( "Demo", SomeEngineSpecAttribute.ImplBinding.DifferentImpl, 3712 )]
        [SomeChildEngine( "Demo-1" )]
        public void UsingDifferentImpl() { }
    }

    [TestCase( "UsingBaseImpl" )]
    [TestCase( "UsingSpecializedImpl" )]
    [TestCase( "UsingDifferentImpl" )]
    public void Impl_binding_and_specialization( string binding )
    {
        var c = new GlobalTypeCache();
        var t = c.Get( typeof( BindingDemo ) );
        var method = t.DeclaredMembers.Single( m => m.Name == binding );
        method.TryGetAllAttributes( TestHelper.Monitor, out var attributes ).ShouldBeTrue();

        attributes.Length.ShouldBe( 2 );
        var primary = attributes[0].ShouldBeAssignableTo<EngineAttributeImpl>();
        primary.ChildrenImpl.Count.ShouldBe( 1 );
        primary.ChildrenImpl.Single().ShouldBeAssignableTo<EngineAttributeImpl>();

        var action = attributes.OfType<ISomeEngineSpecBehavior>()
                               .Single()
                               .DoSomethingWithTheSpecAttribute();
        var expectedAction = binding switch
        {
            "UsingBaseImpl" => "My Attribute is a Spec and it has 3712.",
            "UsingSpecializedImpl" => "I'm the [Spec] implementation, I can do stuff with 3712.",
            _ => "Totally independent [Spec] implementation can do anything with 3712."
        };
        action.ShouldBe( expectedAction );
    }
}
