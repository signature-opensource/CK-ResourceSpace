using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.Engine.TypeCollector.Tests
{
    using Mapping;

    [TestFixture]
    public class MappingTests
    {
        static CachedTypeMapping Get( GlobalTypeCache cache, Type t )
        {
            cache.Get( t ).TryGetMapping( TestHelper.Monitor, out var m ).ShouldBeTrue();
            return m!;
        }

        static string[] Names( System.Collections.Immutable.ImmutableArray<ICachedType> keys ) => keys.Select( k => k.Type.Name ).Order().ToArray();

        [Test]
        public void the_cardinality_is_declared_or_inherited()
        {
            var cache = new GlobalTypeCache();
            Get( cache, typeof( IMulti ) ).Cardinality.ShouldBe( MappingCardinality.Multiple );
            Get( cache, typeof( IMultiSub ) ).Cardinality.ShouldBe( MappingCardinality.Multiple );
            Get( cache, typeof( ISingleUnderMulti ) ).Cardinality.ShouldBe( MappingCardinality.Single );
            Get( cache, typeof( ISingleUnderMultiSub ) ).Cardinality.ShouldBe( MappingCardinality.Single );
            Get( cache, typeof( INone ) ).ShouldBeSameAs( CachedTypeMapping.Empty );
            // Under a single and a multiple base: there cannot be multiple implementations of the single one.
            Get( cache, typeof( IUnderSingleAndMulti ) ).Cardinality.ShouldBe( MappingCardinality.Single );
            foreach( var t in new[] { typeof( IMulti ), typeof( IMultiSub ), typeof( ISingleUnderMulti ), typeof( ISingleUnderMultiSub ) } )
            {
                Get( cache, t ).IsKey.ShouldBeTrue();
            }
        }

        [Test]
        public void a_concrete_class_is_mapped_to_all_its_keys()
        {
            var cache = new GlobalTypeCache();
            var m = Get( cache, typeof( ImplOfSingleUnderMultiSub ) );
            m.IsKey.ShouldBeFalse();
            Names( m.SingleKeys ).ShouldBe( ["ISingleUnderMulti", "ISingleUnderMultiSub"] );
            Names( m.MultipleKeys ).ShouldBe( ["IMulti"] );
            // Abstract classes are abstractions too.
            Get( cache, typeof( MultiBase ) ).IsKey.ShouldBeTrue();
            Get( cache, typeof( MidBase ) ).IsKey.ShouldBeTrue();
            var c = Get( cache, typeof( ConcreteOfMidBase ) );
            Names( c.MultipleKeys ).ShouldBe( ["MidBase", "MultiBase"] );
            c.SingleKeys.ShouldBeEmpty();
        }

        [Test]
        public void not_mapped_revokes_the_type_only()
        {
            var cache = new GlobalTypeCache();
            var family = Get( cache, typeof( IFamily ) );
            family.Cardinality.ShouldBe( MappingCardinality.Single );
            family.IsNotMapped.ShouldBeTrue();
            family.IsKey.ShouldBeFalse();
            Get( cache, typeof( IFamilyMember ) ).IsKey.ShouldBeTrue();
            Names( Get( cache, typeof( Member ) ).SingleKeys ).ShouldBe( ["IFamilyMember"] );

            var manual = Get( cache, typeof( ManualMember ) );
            manual.IsNotMapped.ShouldBeTrue();
            manual.SingleKeys.ShouldBeEmpty();
            Names( Get( cache, typeof( SubOfManualMember ) ).SingleKeys ).ShouldBe( ["IFamilyMember"] );
        }

        [Test]
        public void a_generic_type_definition_is_not_a_key()
        {
            var cache = new GlobalTypeCache();
            var def = Get( cache, typeof( IGenericMulti<> ) );
            def.Cardinality.ShouldBe( MappingCardinality.Multiple );
            def.IsKey.ShouldBeFalse();
            Get( cache, typeof( IGenericMulti<int> ) ).IsKey.ShouldBeTrue();
            Names( Get( cache, typeof( ImplOfGenericMulti ) ).MultipleKeys ).ShouldBe( ["IGenericMulti`1"] );
        }

        [TestCase( typeof( IBothDeclared ), "cannot have both a single mapping ([CK.Engine.TypeCollector.Tests.Mapping.SingleAttribute]) and a multiple mapping ([CK.Core.IsMultipleAttribute])." )]
        [TestCase( typeof( IMultiUnderSingle ), "cannot have a multiple mapping ([CK.Core.IsMultipleAttribute]): it specializes 'CK.Engine.TypeCollector.Tests.Mapping.ISingle' that has a single mapping." )]
        [TestCase( typeof( ConcreteDeclares ), "cannot declare a mapping cardinality ([CK.Engine.TypeCollector.Tests.Mapping.SingleAttribute]): only an interface or an abstract class can." )]
        public void mapping_errors_are_logged_once( Type t, string message )
        {
            var cache = new GlobalTypeCache();
            var cT = cache.Get( t );
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                cT.TryGetMapping( TestHelper.Monitor, out _ ).ShouldBeFalse();
                logs.ShouldContain( l => l.Contains( message ) );
            }
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                cT.TryGetMapping( TestHelper.Monitor, out _ ).ShouldBeFalse();
                logs.ShouldBeEmpty();
            }
        }

        [Test]
        public void a_failed_base_fails_its_specializations()
        {
            var cache = new GlobalTypeCache();
            cache.Get( typeof( ImplOfBothDeclared ) ).TryGetMapping( TestHelper.Monitor, out _ ).ShouldBeFalse();
        }
    }
}

namespace CK.Engine.TypeCollector.Tests.Mapping
{
    [AttributeUsage( AttributeTargets.Class | AttributeTargets.Interface, Inherited = false )]
    public sealed class SingleAttribute : Attribute, ISingleMappingAttribute { }

    [AttributeUsage( AttributeTargets.Class | AttributeTargets.Interface, Inherited = false )]
    public sealed class NotMappedAttribute : Attribute, INotMappedAttribute { }

    public interface INone { }

    [IsMultiple] public interface IMulti { }
    public interface IMultiSub : IMulti { }
    [Single] public interface ISingleUnderMulti : IMulti { }
    public interface ISingleUnderMultiSub : ISingleUnderMulti { }
    public sealed class ImplOfSingleUnderMultiSub : ISingleUnderMultiSub { }

    [Single] public interface ISingle { }
    public interface IUnderSingleAndMulti : ISingle, IMulti { }

    [IsMultiple] public abstract class MultiBase { }
    public abstract class MidBase : MultiBase { }
    public sealed class ConcreteOfMidBase : MidBase { }

    [Single, NotMapped] public interface IFamily { }
    public interface IFamilyMember : IFamily { }
    public sealed class Member : IFamilyMember { }
    [NotMapped] public class ManualMember : IFamilyMember { }
    public sealed class SubOfManualMember : ManualMember { }

    [IsMultiple] public interface IGenericMulti<T> { }
    public sealed class ImplOfGenericMulti : IGenericMulti<int> { }

    [Single, IsMultiple] public interface IBothDeclared { }
    public sealed class ImplOfBothDeclared : IBothDeclared { }
    [IsMultiple] public interface IMultiUnderSingle : ISingle { }
    [Single] public class ConcreteDeclares { }
}
