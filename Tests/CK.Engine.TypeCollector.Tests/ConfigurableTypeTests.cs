using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using static CK.Testing.MonitorTestHelper;

namespace CK.Engine.TypeCollector.Tests;

[TestFixture]
public class ConfigurableTypeTests
{
    [AttributeUsage( AttributeTargets.Class, AllowMultiple = true, Inherited = true )]
    sealed class ConfigurableAttribute : Attribute, IConfigurableTypeAttribute
    {
        public ConfigurableAttribute( string name, TypeActivation activation )
        {
            Name = name;
            Activation = activation;
        }

        public string Name { get; }

        public TypeActivation Activation { get; }
    }

    [AttributeUsage( AttributeTargets.Class, AllowMultiple = true, Inherited = false )]
    sealed class OptionalAttribute : Attribute, IOptionalResourceGroupAttribute
    {
        public OptionalAttribute( bool isOptional ) => IsOptional = isOptional;

        public bool IsOptional { get; }
    }

    [Configurable( "Http.Endpoint", TypeActivation.Optional )]
    public class OptionalFeature : IResourceGroup { }

    [Configurable( "Mail", TypeActivation.ByDefault )]
    public class DefaultFeature : IResourceGroup { }

    [Configurable( "Core", TypeActivation.Required )]
    public class RequiredFeature : IResourceGroup { }

    // The name is not inherited: a specialization is not a configurable type (but it inherits the optionality).
    public class SpecializedFeature : OptionalFeature { }

    public class PlainPackage : IResourceGroup { }

    [Optional( true )]
    public abstract class OptionalBase : IResourceGroup { }

    public class OptionalByBase : OptionalBase { }

    [Optional( false )]
    public class NotOptionalAnymore : OptionalBase { }

    [Optional( true ), Optional( false )]
    public class Disagreement : IResourceGroup { }

    [Configurable( "Abstract", TypeActivation.Optional )]
    public abstract class AbstractConfigurable : IResourceGroup { }

    [Configurable( "NoPackage", TypeActivation.Optional )]
    public class NotAPackage { }

    [Configurable( "2Bad", TypeActivation.Optional )]
    public class BadName : IResourceGroup { }

    [Configurable( "Undeclared", TypeActivation.None )]
    public class NoActivation : IResourceGroup { }

    [Configurable( "One", TypeActivation.Optional ), Configurable( "Two", TypeActivation.Optional )]
    public class TwoNames : IResourceGroup { }

    static IConfigurableTypeAttribute? Get( GlobalTypeCache cache, Type t )
    {
        cache.Get( t ).TryGetConfigurableType( TestHelper.Monitor, out var c ).ShouldBeTrue();
        return c;
    }

    [Test]
    public void a_configurable_type_has_a_name_and_an_activation()
    {
        var cache = new GlobalTypeCache();
        var o = Get( cache, typeof( OptionalFeature ) ).ShouldNotBeNull();
        o.Name.ShouldBe( "Http.Endpoint" );
        o.Activation.ShouldBe( TypeActivation.Optional );
        Get( cache, typeof( DefaultFeature ) ).ShouldNotBeNull().Activation.ShouldBe( TypeActivation.ByDefault );
        Get( cache, typeof( RequiredFeature ) ).ShouldNotBeNull().Activation.ShouldBe( TypeActivation.Required );
        Get( cache, typeof( PlainPackage ) ).ShouldBeNull();
        Get( cache, typeof( SpecializedFeature ) ).ShouldBeNull();
    }

    [Test]
    public void the_optionality_of_a_configurable_type_is_its_activation()
    {
        var cache = new GlobalTypeCache();
        cache.Get( typeof( OptionalFeature ) ).IsOptional.ShouldBeTrue();
        cache.Get( typeof( DefaultFeature ) ).IsOptional.ShouldBeFalse();
        cache.Get( typeof( RequiredFeature ) ).IsOptional.ShouldBeFalse();
        cache.Get( typeof( SpecializedFeature ) ).IsOptional.ShouldBeTrue();
    }

    [Test]
    public void the_optionality_is_the_closest_declaration_in_the_base_classes()
    {
        var cache = new GlobalTypeCache();
        cache.Get( typeof( PlainPackage ) ).IsOptional.ShouldBeFalse();
        cache.Get( typeof( OptionalByBase ) ).IsOptional.ShouldBeTrue();
        cache.Get( typeof( NotOptionalAnymore ) ).IsOptional.ShouldBeFalse();
        cache.Get( typeof( Disagreement ) ).IsOptional.ShouldBeFalse();
    }

    [TestCase( typeof( AbstractConfigurable ), "it is not a package (a concrete IResourceGroup)" )]
    [TestCase( typeof( NotAPackage ), "it is not a package (a concrete IResourceGroup)" )]
    [TestCase( typeof( BadName ), "The configuration name '2Bad'" )]
    [TestCase( typeof( NoActivation ), "must be Optional, ByDefault or Required (it is 'None')" )]
    [TestCase( typeof( TwoNames ), "has 2 configurable type attributes" )]
    public void an_invalid_declaration_is_an_error_logged_once( Type t, string expected )
    {
        var cache = new GlobalTypeCache();
        var type = cache.Get( t );
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            type.TryGetConfigurableType( TestHelper.Monitor, out var c ).ShouldBeFalse();
            c.ShouldBeNull();
            texts.ShouldContain( m => m.Contains( expected ) );
        }
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            type.TryGetConfigurableType( TestHelper.Monitor, out _ ).ShouldBeFalse();
            texts.ShouldBeEmpty();
        }
    }

    [TestCase( "HttpEndpoint", true )]
    [TestCase( "Cris.Sender2", true )]
    [TestCase( "2Cris", false )]
    [TestCase( "Cris.", false )]
    [TestCase( "Cris..Sender", false )]
    [TestCase( "Cris-Sender", false )]
    [TestCase( "Full.Name, Assembly", false )]
    [TestCase( "", false )]
    [TestCase( null, false )]
    public void configuration_names_are_identifiers( string? name, bool valid )
    {
        ICachedType.IsValidConfigurationName( name ).ShouldBe( valid );
    }
}
