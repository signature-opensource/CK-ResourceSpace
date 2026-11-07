using CK.Core;
using CK.Engine.TypeCollector;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.ResourceSpace.Tests
{
    using Scope;

    [TestFixture]
    public class ScopeTests
    {
        // The compatibility rule of the "options" space: only the IOption types concern it.
        sealed class OptionScope : IResPackageDescriptorResolver
        {
            public bool IsCompatible( ICachedType type ) => typeof( IOption ).IsAssignableFrom( type.Type );

            public bool ResolveRequired( IActivityMonitor monitor, IResPackageDescriptorRegistrar registrar, ResPackageDescriptor.Ref reference )
            {
                monitor.Error( $"Unexpected resolution of '{reference}'." );
                return false;
            }
        }

        static readonly Type[] _options = [typeof( OptionA ), typeof( OptionB ), typeof( LastOption ),
                                           typeof( OptionInOtherPackage ), typeof( OptionRequiringOtherPlugins )];

        static ResCoreData? Build( ResSpaceConfiguration config )
        {
            var collector = config.Build( TestHelper.Monitor ).ShouldNotBeNull();
            return new ResSpaceDataBuilder( collector ).Build( TestHelper.Monitor )?.CoreData;
        }

        static string[] Order( ResCoreData data ) => data.Packages.Where( p => p.Type != null ).Select( p => p.Type!.Type.Name ).ToArray();

        [TestCase( true )]
        [TestCase( false )]
        public void references_to_incompatible_types_are_ignored( bool revertOrdering )
        {
            var scope = new OptionScope();
            var config = new ResSpaceConfiguration { PackageResolver = scope, RevertOrderingNames = revertOrdering };
            foreach( var t in _options ) config.RegisterPackage( TestHelper.Monitor, t, isOptional: false ).ShouldNotBeNull();
            var data = Build( config ).ShouldNotBeNull();
            // OptionA requires OtherPackage (ignored) and OptionB. LastOption requires all the IEndpointOption.
            // OptionInOtherPackage's package and OptionRequiringOtherPlugins' requirement are ignored.
            var order = Order( data ).ToList();
            order.ShouldBe( _options.Select( t => t.Name ), ignoreOrder: true );
            order.IndexOf( "OptionB" ).ShouldBeLessThan( order.IndexOf( "OptionA" ) );
            order.IndexOf( "OptionA" ).ShouldBeLessThan( order.IndexOf( "LastOption" ) );

            // The closure applies the same rule.
            var cache = config.TypeCache;
            ResPackageClosure.Compute( TestHelper.Monitor, cache, _options.Select( cache.Get ), _ => true, scope.IsCompatible )
                             .ShouldNotBeNull()
                             .Select( t => t.Type.Name )
                             .ShouldBe( order, ignoreOrder: true );
        }

        [Test]
        public void without_the_rule_every_type_is_compatible()
        {
            var config = new ResSpaceConfiguration();
            config.RegisterPackage( TestHelper.Monitor, typeof( OptionA ), isOptional: false ).ShouldNotBeNull();
            config.RegisterPackage( TestHelper.Monitor, typeof( OptionB ), isOptional: false ).ShouldNotBeNull();
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                Build( config ).ShouldBeNull();
                logs.ShouldContain( l => l.Contains( "No configured PackageResolver" ) && l.Contains( "OtherPackage" ) );
            }
            var cache = new GlobalTypeCache();
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                ResPackageClosure.Compute( TestHelper.Monitor, cache, [cache.Get( typeof( OptionA ) ), cache.Get( typeof( OptionB ) )], _ => true )
                                 .ShouldBeNull();
                logs.ShouldContain( "'CK.ResourceSpace.Tests.Scope.OptionA' references 'CK.ResourceSpace.Tests.Scope.OtherPackage' (Requires) that is not a candidate package." );
            }
        }

        [Test]
        public void registering_an_incompatible_type_is_an_error()
        {
            var scope = new OptionScope();
            var config = new ResSpaceConfiguration { PackageResolver = scope };
            config.RegisterPackage( TestHelper.Monitor, typeof( OptionB ), isOptional: false ).ShouldNotBeNull();
            config.RegisterPackage( TestHelper.Monitor, typeof( OtherPackage ), isOptional: true ).ShouldNotBeNull();
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                Build( config ).ShouldBeNull();
                logs.ShouldContain( "Type 'CK.ResourceSpace.Tests.Scope.OtherPackage' is registered but it is not compatible with this space (see IResPackageDescriptorResolver.IsCompatible)." );
            }
            var cache = new GlobalTypeCache();
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                ResPackageClosure.Compute( TestHelper.Monitor, cache, [cache.Get( typeof( OtherPackage ) )], _ => true, scope.IsCompatible )
                                 .ShouldBeNull();
                logs.ShouldContain( "Candidate package 'CK.ResourceSpace.Tests.Scope.OtherPackage' is not compatible with this space (see IResPackageDescriptorResolver.IsCompatible)." );
            }
        }

        [Test]
        public void a_required_reference_to_a_compatible_type_that_is_not_allowed_is_an_error()
        {
            var config = new ResSpaceConfiguration( new GlobalTypeCache(), t => t.Type != typeof( NotAllowedOption ) && t.Type != typeof( OtherPackage ) )
            {
                PackageResolver = new OptionScope()
            };
            config.RegisterPackage( TestHelper.Monitor, typeof( RequiresNotAllowed ), isOptional: false ).ShouldNotBeNull();
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                Build( config ).ShouldBeNull();
                logs.ShouldContain( "The Requires reference to 'CK.ResourceSpace.Tests.Scope.NotAllowedOption' cannot be resolved: this type is not allowed in this space." );
            }
        }

        [Test]
        public void an_optional_reference_to_a_type_that_is_not_allowed_and_an_incompatible_one_are_ignored()
        {
            var config = new ResSpaceConfiguration( new GlobalTypeCache(), t => t.Type != typeof( NotAllowedOption ) && t.Type != typeof( OtherPackage ) )
            {
                PackageResolver = new OptionScope()
            };
            // OptionA requires OtherPackage: not allowed, but incompatible (out of scope).
            config.RegisterPackage( TestHelper.Monitor, typeof( OptionA ), isOptional: false ).ShouldNotBeNull();
            config.RegisterPackage( TestHelper.Monitor, typeof( OptionB ), isOptional: false ).ShouldNotBeNull();
            config.RegisterPackage( TestHelper.Monitor, typeof( OptionallyRequiresNotAllowed ), isOptional: false ).ShouldNotBeNull();
            Order( Build( config ).ShouldNotBeNull() ).ShouldBe( ["OptionB", "OptionA", "OptionallyRequiresNotAllowed"], ignoreOrder: true );
        }
    }
}

namespace CK.ResourceSpace.Tests.Scope
{
    // The options are the IOption types (the compatibility rule of their space).
    public interface IOption : IResourcePackage { }
    [IsMultiple] public interface IEndpointOption : IOption { }

    // Another space's types.
    public interface IOther : IResourcePackage { }
    [EmbeddedResourceType] public sealed class OtherPackage : IOther { }
    [IsMultiple] public interface IOtherPlugin : IOther { }
    [EmbeddedResourceType] public sealed class OtherPlugin : IOtherPlugin { }

    [EmbeddedResourceType, Requires<OtherPackage, OptionB>] public sealed class OptionA : IEndpointOption { }
    [EmbeddedResourceType] public sealed class OptionB : IEndpointOption { }
    [EmbeddedResourceType, Requires<IEndpointOption>] public sealed class LastOption : IOption { }
    [EmbeddedResourceType, Package<OtherPackage>] public sealed class OptionInOtherPackage : IOption { }
    [EmbeddedResourceType, Requires<IOtherPlugin>] public sealed class OptionRequiringOtherPlugins : IOption { }

    [EmbeddedResourceType] public sealed class NotAllowedOption : IOption { }
    [EmbeddedResourceType, Requires<NotAllowedOption>] public sealed class RequiresNotAllowed : IOption { }
    [EmbeddedResourceType, OptionalRequires<NotAllowedOption>] public sealed class OptionallyRequiresNotAllowed : IOption { }
}
