using CK.Core;
using CK.Engine.TypeCollector;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.ResourceSpace.Tests
{
    using Closure;

    [TestFixture]
    public class ResPackageClosureTests
    {
        // Registers the candidates (the roots as required, the other ones as optional), builds the ResSpace data,
        // and checks that the closure is the set of kept typed packages.
        internal static HashSet<ICachedType> CheckSameAsResSpace( Type[] candidates, Type[] roots ) => CheckSameAsResSpace( candidates, roots, out _ );

        internal static HashSet<ICachedType> CheckSameAsResSpace( Type[] candidates, Type[] roots, out ResCoreData coreData )
        {
            var config = new ResSpaceConfiguration();
            var cache = config.TypeCache;
            foreach( var t in candidates )
            {
                config.RegisterPackage( TestHelper.Monitor, t, isOptional: !roots.Contains( t ) ).ShouldNotBeNull();
            }
            var collector = config.Build( TestHelper.Monitor ).ShouldNotBeNull();
            coreData = new ResSpaceDataBuilder( collector ).Build( TestHelper.Monitor ).ShouldNotBeNull().CoreData;
            var kept = coreData.Packages.Select( p => p.Type ).Where( t => t != null ).ToHashSet();

            var closure = ResPackageClosure.Compute( TestHelper.Monitor,
                                                     cache,
                                                     candidates.Select( cache.Get ),
                                                     t => roots.Contains( t.Type ) )
                                      .ShouldNotBeNull();
            closure.Select( t => t.Type.Name ).Order().ShouldBe( kept.Select( t => t!.Type.Name ).Order() );
            return closure;
        }

        internal static string[] Names( HashSet<ICachedType> closure ) => closure.Select( t => t.Type.Name ).Order().ToArray();

        static readonly Type[] _all = [typeof( Root ), typeof( A ), typeof( B ), typeof( C ), typeof( G ), typeof( P ),
                                       typeof( D ), typeof( E ), typeof( F ), typeof( H ), typeof( Unrelated )];

        [Test]
        public void closure_follows_the_required_references_forward_only()
        {
            var closure = CheckSameAsResSpace( _all, [typeof( Root )] );
            // B is only optionally required (dropped), D and E only declare a RequiredBy to A (forward only: dropped),
            // Unrelated is not referenced.
            Names( closure ).ShouldBe( ["A", "C", "F", "G", "H", "P", "Root"] );
        }

        [Test]
        public void closure_without_roots_is_empty()
        {
            CheckSameAsResSpace( _all, [] ).ShouldBeEmpty();
        }

        [TestCase( typeof( Closure.ByAttribute.Requires ), typeof( Closure.ByTopology.Requires ) )]
        [TestCase( typeof( Closure.ByAttribute.RequiredBy ), typeof( Closure.ByTopology.RequiredBy ) )]
        [TestCase( typeof( Closure.ByAttribute.Children ), typeof( Closure.ByTopology.Children ) )]
        [TestCase( typeof( Closure.ByAttribute.Groups ), typeof( Closure.ByTopology.Groups ) )]
        [TestCase( typeof( Closure.ByAttribute.Package ), typeof( Closure.ByTopology.Package ) )]
        [TestCase( typeof( Closure.ByAttribute.OptionalRequires ), typeof( Closure.ByTopology.OptionalRequires ) )]
        [TestCase( typeof( Closure.ByAttribute.OptionalRequiredBy ), typeof( Closure.ByTopology.OptionalRequiredBy ) )]
        [TestCase( typeof( Closure.ByAttribute.OptionalChildren ), typeof( Closure.ByTopology.OptionalChildren ) )]
        [TestCase( typeof( Closure.ByAttribute.OptionalGroups ), typeof( Closure.ByTopology.OptionalGroups ) )]
        public void each_topology_member_is_handled_like_its_attribute( Type byAttribute, Type byTopology )
        {
            Type[] targets = [typeof( TargetPackage ), typeof( TargetGroup )];
            var withAttribute = CheckSameAsResSpace( [byAttribute, .. targets], [byAttribute] );
            var withTopology = CheckSameAsResSpace( [byTopology, .. targets], [byTopology] );
            // Each check uses its own type cache: compares the names.
            withTopology.Where( t => t.Type != byTopology ).Select( t => t.Type.Name )
                        .ShouldBe( withAttribute.Where( t => t.Type != byAttribute ).Select( t => t.Type.Name ), ignoreOrder: true );
            // The optional relations keep nothing.
            (withAttribute.Count == 1).ShouldBe( byAttribute.Name.StartsWith( "Optional" ) );
        }

        [Test]
        public void a_required_reference_to_a_non_candidate_is_an_error()
        {
            var cache = new GlobalTypeCache();
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                // A requires C (by its Children) that is not a candidate.
                ResPackageClosure.Compute( TestHelper.Monitor, cache, [cache.Get( typeof( A ) )], _ => true ).ShouldBeNull();
                logs.ShouldContain( "'CK.ResourceSpace.Tests.Closure.A' references 'CK.ResourceSpace.Tests.Closure.C' (Children) that is not a candidate package." );
            }
        }

        [Test]
        public void at_most_one_package_can_be_declared()
        {
            var config = new ResSpaceConfiguration();
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                config.RegisterPackage( TestHelper.Monitor, typeof( TwoPackages ) ).ShouldBeNull();
                logs.ShouldContain( "At most one package can be declared for type 'CK.ResourceSpace.Tests.Closure.TwoPackages' (by [Package<T>], [Package( \"FullName\" )] or a IResourceGroupTopologyAttribute)." );
            }
        }

        [Test]
        public void a_topology_type_must_be_a_IResourceGroup()
        {
            var config = new ResSpaceConfiguration();
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                config.RegisterPackage( TestHelper.Monitor, typeof( InvalidTopology ) ).ShouldBeNull();
                logs.ShouldContain( "Invalid Requires 'string' declared by [CK.ResourceSpace.Tests.Closure.TopologyAttribute] on 'CK.ResourceSpace.Tests.Closure.InvalidTopology': it is not a IResourceGroup." );
            }
        }

        [Test]
        public void a_same_target_declared_twice_is_registered_once_and_required_wins()
        {
            var config = new ResSpaceConfiguration();
            var d = config.RegisterPackage( TestHelper.Monitor, typeof( TwiceRequires ) ).ShouldNotBeNull();
            d.Requires.Count.ShouldBe( 1 );
            d.Requires[0].IsOptional.ShouldBeFalse();
        }

        [TestCase( true )]
        [TestCase( false )]
        public void name_based_attributes_are_read( bool revertOrdering )
        {
            // NameBasedA has [Requires( "...NameBasedC" )] and [OptionalRequires( "...NameBasedB" )]: B is ordered before A
            // when both are kept.
            Type[] all = [typeof( NameBasedA ), typeof( NameBasedB ), typeof( NameBasedC )];
            var config = new ResSpaceConfiguration { RevertOrderingNames = revertOrdering };
            foreach( var t in all ) config.RegisterPackage( TestHelper.Monitor, t, isOptional: false ).ShouldNotBeNull();
            var coreData = new ResSpaceDataBuilder( config.Build( TestHelper.Monitor ).ShouldNotBeNull() ).Build( TestHelper.Monitor ).ShouldNotBeNull().CoreData;
            var names = coreData.Packages.Select( p => p.Type?.Type.Name ).Where( n => n != null ).ToList();
            names.IndexOf( "NameBasedB" ).ShouldBeLessThan( names.IndexOf( "NameBasedA" ) );
            names.IndexOf( "NameBasedC" ).ShouldBeLessThan( names.IndexOf( "NameBasedA" ) );

            // Only NameBasedA as a root: C is required, B is optional (dropped).
            Names( CheckSameAsResSpace( all, [typeof( NameBasedA )] ) ).ShouldBe( ["NameBasedA", "NameBasedC"] );
        }

        [Test]
        public void the_name_mappings_of_a_dropped_optional_package_are_removed()
        {
            var config = new ResSpaceConfiguration();
            config.RegisterPackage( TestHelper.Monitor, typeof( Unrelated ), isOptional: false ).ShouldNotBeNull();
            var dropped = config.RegisterPackage( TestHelper.Monitor, typeof( B ), isOptional: true ).ShouldNotBeNull();
            dropped.AddSingleMapping( TestHelper.Monitor, "SomeAlias" ).ShouldBeTrue();
            dropped.AddSingleMapping( TestHelper.Monitor, "OtherAlias" ).ShouldBeTrue();

            var collector = config.Build( TestHelper.Monitor ).ShouldNotBeNull();
            var coreData = new ResSpaceDataBuilder( collector ).Build( TestHelper.Monitor ).ShouldNotBeNull().CoreData;
            collector.PackageIndex.ContainsKey( "SomeAlias" ).ShouldBeFalse();
            collector.PackageIndex.ContainsKey( "OtherAlias" ).ShouldBeFalse();
            coreData.PackageIndex.ContainsKey( "SomeAlias" ).ShouldBeFalse();
        }
    }
}

namespace CK.ResourceSpace.Tests.Closure
{
    sealed class TopologyAttribute : Attribute, IResourceGroupTopologyAttribute
    {
        public Type[] Requires { get; set; } = [];
        public Type[] OptionalRequires { get; set; } = [];
        public Type[] RequiredBy { get; set; } = [];
        public Type[] OptionalRequiredBy { get; set; } = [];
        public Type[] Children { get; set; } = [];
        public Type[] OptionalChildren { get; set; } = [];
        public Type[] Groups { get; set; } = [];
        public Type[] OptionalGroups { get; set; } = [];
        public Type? Package { get; set; }

        ReadOnlySpan<Type> IResourceGroupTopologyAttribute.Requires => Requires;
        ReadOnlySpan<Type> IResourceGroupTopologyAttribute.OptionalRequires => OptionalRequires;
        ReadOnlySpan<Type> IResourceGroupTopologyAttribute.RequiredBy => RequiredBy;
        ReadOnlySpan<Type> IResourceGroupTopologyAttribute.OptionalRequiredBy => OptionalRequiredBy;
        ReadOnlySpan<Type> IResourceGroupTopologyAttribute.Children => Children;
        ReadOnlySpan<Type> IResourceGroupTopologyAttribute.OptionalChildren => OptionalChildren;
        ReadOnlySpan<Type> IResourceGroupTopologyAttribute.Groups => Groups;
        ReadOnlySpan<Type> IResourceGroupTopologyAttribute.OptionalGroups => OptionalGroups;
        Type? IResourceGroupTopologyAttribute.Package => Package;
    }

    // Root -Requires-> A, Root -OptionalRequires-> B, A -Children-> C, C -Groups-> G, A -Package-> P,
    // A -Topology.Requires-> H, H -RequiredBy-> F (forward: F is kept), D and E -RequiredBy-> A (reverse: not kept).
    [EmbeddedResourceType, Requires<A>, OptionalRequires<B>] sealed class Root : IResourcePackage { }
    [EmbeddedResourceType, Children<C>, Package<P>, Topology( Requires = [typeof( H )] )] sealed class A : IResourcePackage { }
    [EmbeddedResourceType] sealed class B : IResourcePackage { }
    [EmbeddedResourceType, Groups<G>] sealed class C : IResourcePackage { }
    [EmbeddedResourceType] sealed class G : IResourceGroup { }
    [EmbeddedResourceType] sealed class P : IResourcePackage { }
    [EmbeddedResourceType, RequiredBy<A>] sealed class D : IResourcePackage { }
    [EmbeddedResourceType, OptionalRequiredBy<A>] sealed class E : IResourcePackage { }
    [EmbeddedResourceType] sealed class F : IResourcePackage { }
    [EmbeddedResourceType, RequiredBy<F>] sealed class H : IResourcePackage { }
    [EmbeddedResourceType] sealed class Unrelated : IResourcePackage { }

    [EmbeddedResourceType] sealed class TargetPackage : IResourcePackage { }
    [EmbeddedResourceType] sealed class TargetGroup : IResourceGroup { }

    namespace ByAttribute
    {
        [EmbeddedResourceType, Requires<TargetPackage>] sealed class Requires : IResourcePackage { }
        [EmbeddedResourceType, RequiredBy<TargetPackage>] sealed class RequiredBy : IResourcePackage { }
        [EmbeddedResourceType, Children<TargetPackage>] sealed class Children : IResourceGroup { }
        [EmbeddedResourceType, Groups<TargetGroup>] sealed class Groups : IResourcePackage { }
        [EmbeddedResourceType, Package<TargetPackage>] sealed class Package : IResourcePackage { }
        [EmbeddedResourceType, OptionalRequires<TargetPackage>] sealed class OptionalRequires : IResourcePackage { }
        [EmbeddedResourceType, OptionalRequiredBy<TargetPackage>] sealed class OptionalRequiredBy : IResourcePackage { }
        [EmbeddedResourceType, OptionalChildren<TargetPackage>] sealed class OptionalChildren : IResourceGroup { }
        [EmbeddedResourceType, OptionalGroups<TargetGroup>] sealed class OptionalGroups : IResourcePackage { }
    }

    namespace ByTopology
    {
        [EmbeddedResourceType, Topology( Requires = [typeof( TargetPackage )] )] sealed class Requires : IResourcePackage { }
        [EmbeddedResourceType, Topology( RequiredBy = [typeof( TargetPackage )] )] sealed class RequiredBy : IResourcePackage { }
        [EmbeddedResourceType, Topology( Children = [typeof( TargetPackage )] )] sealed class Children : IResourceGroup { }
        [EmbeddedResourceType, Topology( Groups = [typeof( TargetGroup )] )] sealed class Groups : IResourcePackage { }
        [EmbeddedResourceType, Topology( Package = typeof( TargetPackage ) )] sealed class Package : IResourcePackage { }
        [EmbeddedResourceType, Topology( OptionalRequires = [typeof( TargetPackage )] )] sealed class OptionalRequires : IResourcePackage { }
        [EmbeddedResourceType, Topology( OptionalRequiredBy = [typeof( TargetPackage )] )] sealed class OptionalRequiredBy : IResourcePackage { }
        [EmbeddedResourceType, Topology( OptionalChildren = [typeof( TargetPackage )] )] sealed class OptionalChildren : IResourceGroup { }
        [EmbeddedResourceType, Topology( OptionalGroups = [typeof( TargetGroup )] )] sealed class OptionalGroups : IResourcePackage { }
    }


    [EmbeddedResourceType, Package<P>, Topology( Package = typeof( P ) )] sealed class TwoPackages : IResourcePackage { }
    [EmbeddedResourceType, Topology( Requires = [typeof( string )] )] sealed class InvalidTopology : IResourcePackage { }
    [EmbeddedResourceType, Requires<TargetPackage>, Topology( OptionalRequires = [typeof( TargetPackage )], Requires = [typeof( TargetPackage )] )]
    sealed class TwiceRequires : IResourcePackage { }

    [EmbeddedResourceType, Requires( "CK.ResourceSpace.Tests.Closure.NameBasedC" ), OptionalRequires( "CK.ResourceSpace.Tests.Closure.NameBasedB" )]
    sealed class NameBasedA : IResourcePackage { }
    [EmbeddedResourceType] sealed class NameBasedB : IResourcePackage { }
    [EmbeddedResourceType] sealed class NameBasedC : IResourcePackage { }
}
