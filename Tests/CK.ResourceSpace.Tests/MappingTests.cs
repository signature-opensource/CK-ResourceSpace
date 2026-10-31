using CK.BinarySerialization;
using CK.Core;
using CK.Engine.TypeCollector;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.ResourceSpace.Tests
{
    using Mappings;
    using static ResPackageClosureTests;

    [TestFixture]
    public class MappingTests
    {
        static ResSpaceConfiguration Register( params (Type Type, bool Required)[] packages )
        {
            var config = new ResSpaceConfiguration();
            foreach( var (t, required) in packages )
            {
                config.RegisterPackage( TestHelper.Monitor, t, isOptional: !required ).ShouldNotBeNull();
            }
            return config;
        }

        static ResCoreData? Build( ResSpaceConfiguration config )
        {
            var collector = config.Build( TestHelper.Monitor ).ShouldNotBeNull();
            return new ResSpaceDataBuilder( collector ).Build( TestHelper.Monitor )?.CoreData;
        }

        static string[] Order( ResCoreData data ) => data.Packages.Where( p => p.Type != null ).Select( p => p.Type!.Type.Name ).ToArray();

        [Test]
        public void a_single_abstraction_is_resolved_to_its_only_implementation()
        {
            var closure = CheckSameAsResSpace( [typeof( LoginPage ), typeof( PasswordLogin )], [typeof( LoginPage )], out var data );
            Names( closure ).ShouldBe( ["LoginPage", "PasswordLogin"] );
            data.PackageIndex[data.TypeCache.Get( typeof( ILogin ) )].Type!.Type.ShouldBe( typeof( PasswordLogin ) );
            Order( data ).ShouldBe( ["PasswordLogin", "LoginPage"] );
        }

        [Test]
        public void the_required_implementation_of_a_single_abstraction_wins()
        {
            var closure = CheckSameAsResSpace( [typeof( LoginPage ), typeof( PasswordLogin ), typeof( PasskeyLogin )],
                                               [typeof( LoginPage ), typeof( PasskeyLogin )],
                                               out var data );
            Names( closure ).ShouldBe( ["LoginPage", "PasskeyLogin"] );
            data.PackageIndex[data.TypeCache.Get( typeof( ILogin ) )].Type!.Type.ShouldBe( typeof( PasskeyLogin ) );
        }

        [Test]
        public void an_ambiguous_single_abstraction_is_an_error()
        {
            var config = Register( (typeof( LoginPage ), true), (typeof( PasswordLogin ), false), (typeof( PasskeyLogin ), false) );
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                Build( config ).ShouldBeNull();
                logs.ShouldContain( l => l.StartsWith( "Ambiguous single abstraction 'CK.ResourceSpace.Tests.Mappings.ILogin'" ) );
            }
            var cache = new GlobalTypeCache();
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                ResPackageClosure.Compute( TestHelper.Monitor,
                                           cache,
                                           [cache.Get( typeof( LoginPage ) ), cache.Get( typeof( PasswordLogin ) ), cache.Get( typeof( PasskeyLogin ) )],
                                           t => t.Type == typeof( LoginPage ) ).ShouldBeNull();
                logs.ShouldContain( l => l.StartsWith( "Ambiguous single abstraction 'CK.ResourceSpace.Tests.Mappings.ILogin'" ) );
            }
        }

        [Test]
        public void a_single_abstraction_cannot_have_two_kept_implementations()
        {
            var config = Register( (typeof( PasswordLogin ), true), (typeof( PasskeyLogin ), true) );
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                Build( config ).ShouldBeNull();
                logs.ShouldContain( "'CK.ResourceSpace.Tests.Mappings.ILogin' is a single abstraction but more than one kept package implement it: 'CK.ResourceSpace.Tests.Mappings.PasskeyLogin', 'CK.ResourceSpace.Tests.Mappings.PasswordLogin'." );
            }
        }

        [Test]
        public void a_reference_to_a_multiple_abstraction_never_keeps_a_package()
        {
            Type[] all = [typeof( Consumer ), typeof( PluginA ), typeof( PluginB ), typeof( PluginC )];
            var closure = CheckSameAsResSpace( all, [typeof( Consumer ), typeof( PluginA ), typeof( PluginB )], out var data );
            Names( closure ).ShouldBe( ["Consumer", "PluginA", "PluginB"] );
            // Consumer comes after the kept plugins (its name would put it first).
            Order( data ).ShouldBe( ["PluginA", "PluginB", "Consumer"] );
            data.MultipleMappings[data.TypeCache.Get( typeof( IPlugin ) )].Select( p => p.Type!.Type.Name ).ShouldBe( ["PluginA", "PluginB"] );

            // Without any kept plugin, the abstraction has no mapping.
            CheckSameAsResSpace( all, [typeof( Consumer )], out data );
            data.MultipleMappings.ShouldBeEmpty();
        }

        [Test]
        public void a_group_can_contain_a_multiple_abstraction()
        {
            CheckSameAsResSpace( [typeof( AllPlugins ), typeof( PluginA ), typeof( PluginB )],
                                 [typeof( AllPlugins ), typeof( PluginA ), typeof( PluginB )],
                                 out var data );
            var all = data.PackageIndex[data.TypeCache.Get( typeof( AllPlugins ) )];
            all.Children.Select( p => p.Type!.Type.Name ).ShouldBe( ["PluginA", "PluginB"] );
        }

        [Test]
        public void a_plugin_cannot_require_a_host_that_references_its_abstraction()
        {
            // The plugin is optional (it will not be kept): this is detected anyway.
            var config = Register( (typeof( Host ), true), (typeof( HostPlugin ), false) );
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                Build( config ).ShouldBeNull();
                logs.ShouldContain( l => l.StartsWith( "'CK.ResourceSpace.Tests.Mappings.HostPlugin' requires 'CK.ResourceSpace.Tests.Mappings.Host' that references 'CK.ResourceSpace.Tests.Mappings.IHostPlugin' (in its Children)" ) );
            }
        }

        [Test]
        public void a_cycle_through_a_multiple_abstraction_names_it()
        {
            var config = Register( (typeof( Gatherer ), true), (typeof( Middle ), true), (typeof( CyclingPlugin ), true) );
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                Build( config ).ShouldBeNull();
                logs.ShouldContain( l => l.Contains( "'CK.ResourceSpace.Tests.Mappings.Gatherer' requires 'CK.ResourceSpace.Tests.Mappings.CyclingPlugin' (through 'CK.ResourceSpace.Tests.Mappings.ICyclingPlugin')" ) );
            }
        }

        [Test]
        public void a_not_mapped_optional_package_is_kept_only_by_its_own_type()
        {
            // ThemedPage requires ITheme: DefaultTheme is its only implementation (CustomTheme is not mapped).
            var closure = CheckSameAsResSpace( [typeof( ThemedPage ), typeof( DefaultTheme ), typeof( CustomTheme )], [typeof( ThemedPage )], out var data );
            Names( closure ).ShouldBe( ["DefaultTheme", "ThemedPage"] );
            // UsesCustomTheme requires CustomTheme by its type.
            closure = CheckSameAsResSpace( [typeof( UsesCustomTheme ), typeof( CustomTheme )], [typeof( UsesCustomTheme )], out data );
            Names( closure ).ShouldBe( ["CustomTheme", "UsesCustomTheme"] );
            data.PackageIndex.ContainsKey( data.TypeCache.Get( typeof( ITheme ) ) ).ShouldBeFalse();
        }

        [Test]
        public void a_multiple_abstraction_cannot_be_a_package()
        {
            var config = Register( (typeof( InPluginPackage ), true), (typeof( PluginA ), true) );
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                Build( config ).ShouldBeNull();
                logs.ShouldContain( "The package of a package cannot be 'CK.ResourceSpace.Tests.Mappings.IPlugin' that is a multiple abstraction." );
            }
        }

        [Test]
        public void an_abstraction_cannot_be_registered_as_a_package()
        {
            var config = new ResSpaceConfiguration();
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                config.RegisterPackage( TestHelper.Monitor, typeof( AbstractPlugin ) ).ShouldBeNull();
                logs.ShouldContain( "Type 'CK.ResourceSpace.Tests.Mappings.AbstractPlugin' is an abstraction (it has a Multiple mapping): it cannot be registered as a package." );
            }
        }

        [TestCase( true )]
        [TestCase( false )]
        public void an_optional_reference_registered_before_its_promotion_keeps_its_ordering( bool revertOrdering )
        {
            // AOptional is processed first: its optional reference to ZPromoted (that is still optional) is resolved later,
            // once RPromoter has promoted ZPromoted.
            var config = Register( (typeof( AOptional ), true), (typeof( ZPromoted ), false), (typeof( RPromoter ), true) );
            config.RevertOrderingNames = revertOrdering;
            var data = Build( config ).ShouldNotBeNull();
            var order = Order( data ).ToList();
            order.IndexOf( "ZPromoted" ).ShouldBeLessThan( order.IndexOf( "AOptional" ) );
        }

        [Test]
        public void the_multiple_mappings_are_serialized()
        {
            CheckSameAsResSpace( [typeof( Consumer ), typeof( PluginA ), typeof( PluginB )],
                                 [typeof( Consumer ), typeof( PluginA ), typeof( PluginB )],
                                 out var data );
            var stream = new MemoryStream();
            using( var s = BinarySerializer.Create( stream, new BinarySerializerContext() ) )
            {
                s.WriteObject( data );
            }
            stream.Position = 0;
            var context = new BinaryDeserializerContext();
            context.Services.Add( new GlobalTypeCache() );
            var back = BinaryDeserializer.Deserialize( stream, context, d => d.ReadObject<ResCoreData>() ).GetResult();
            var (key, plugins) = back.MultipleMappings.Single();
            key.Type.ShouldBe( typeof( IPlugin ) );
            plugins.Select( p => p.Type!.Type.Name ).ShouldBe( ["PluginA", "PluginB"] );
        }
    }
}

namespace CK.ResourceSpace.Tests.Mappings
{
    [AttributeUsage( AttributeTargets.Class | AttributeTargets.Interface, Inherited = false )]
    public sealed class SingleAttribute : Attribute, ISingleMappingAttribute { }

    [AttributeUsage( AttributeTargets.Class | AttributeTargets.Interface, Inherited = false )]
    public sealed class NotMappedAttribute : Attribute, INotMappedAttribute { }

    [Single] public interface ILogin : IResourcePackage { }
    [EmbeddedResourceType] public sealed class PasswordLogin : ILogin { }
    [EmbeddedResourceType] public sealed class PasskeyLogin : ILogin { }
    [EmbeddedResourceType, Requires<ILogin>] public sealed class LoginPage : IResourcePackage { }

    [IsMultiple] public interface IPlugin : IResourcePackage { }
    [EmbeddedResourceType] public sealed class PluginA : IPlugin { }
    [EmbeddedResourceType] public sealed class PluginB : IPlugin { }
    [EmbeddedResourceType] public sealed class PluginC : IPlugin { }
    [EmbeddedResourceType, Requires<IPlugin>] public sealed class Consumer : IResourcePackage { }
    [EmbeddedResourceType, Children<IPlugin>] public sealed class AllPlugins : IResourceGroup { }
    [EmbeddedResourceType, Package<IPlugin>] public sealed class InPluginPackage : IResourcePackage { }
    [IsMultiple] public abstract class AbstractPlugin : IResourcePackage { }

    [IsMultiple] public interface IHostPlugin : IResourcePackage { }
    [EmbeddedResourceType, Children<IHostPlugin>] public sealed class Host : IResourceGroup { }
    [EmbeddedResourceType, Requires<Host>] public sealed class HostPlugin : IHostPlugin { }

    // Gatherer requires the plugins, the plugin requires Middle that requires Gatherer.
    [IsMultiple] public interface ICyclingPlugin : IResourcePackage { }
    [EmbeddedResourceType, Requires<ICyclingPlugin>] public sealed class Gatherer : IResourcePackage { }
    [EmbeddedResourceType, Requires<Gatherer>] public sealed class Middle : IResourcePackage { }
    [EmbeddedResourceType, Requires<Middle>] public sealed class CyclingPlugin : ICyclingPlugin { }

    [Single] public interface ITheme : IResourcePackage { }
    [EmbeddedResourceType] public sealed class DefaultTheme : ITheme { }
    [EmbeddedResourceType, NotMapped] public sealed class CustomTheme : ITheme { }
    [EmbeddedResourceType, Requires<ITheme>] public sealed class ThemedPage : IResourcePackage { }
    [EmbeddedResourceType, Requires<CustomTheme>] public sealed class UsesCustomTheme : IResourcePackage { }

    [EmbeddedResourceType, OptionalRequires<ZPromoted>] public sealed class AOptional : IResourcePackage { }
    [EmbeddedResourceType] public sealed class ZPromoted : IResourcePackage { }
    [EmbeddedResourceType, Requires<ZPromoted>] public sealed class RPromoter : IResourcePackage { }
}
