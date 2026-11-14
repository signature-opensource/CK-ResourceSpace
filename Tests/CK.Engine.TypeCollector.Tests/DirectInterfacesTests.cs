using CK.Setup;
using Shouldly;
using NUnit.Framework;
using static CK.Testing.MonitorTestHelper;

namespace CK.Engine.TypeCollector.Tests;

[TestFixture]
public class DirectInterfacesTests
{

    public interface I1 { }

    public class BaseObject : I1 { }

    public class SpecializedObject1 : BaseObject { }

    public class SpecializedObject2 : SpecializedObject1 { }

    [Test]
    public void from_BaseType()
    {
        {
            var typeCache = new GlobalTypeCache();
            typeCache.Get( typeof( SpecializedObject1 ) );
            var o1 = typeCache.Find( typeof( SpecializedObject1 ) ).ShouldNotBeNull();
            var i1 = typeCache.Find( typeof( I1 ) ).ShouldNotBeNull();

            o1.Interfaces.ShouldContain( i1 );
            o1.DirectInterfaces.ShouldNotContain( i1 );
        }
        {
            var typeCache = new GlobalTypeCache();
            typeCache.Get( typeof( SpecializedObject2 ) );


            var o2 = typeCache.Find( typeof( SpecializedObject2 ) ).ShouldNotBeNull();
            var i1 = typeCache.Find( typeof( I1 ) ).ShouldNotBeNull();

            o2.Interfaces.ShouldContain( i1 );
            o2.DirectInterfaces.ShouldNotContain( i1 );

            var o1 = typeCache.Find( typeof( SpecializedObject1 ) ).ShouldNotBeNull();
            o1.Interfaces.ShouldContain( i1 );
            o1.DirectInterfaces.ShouldNotContain( i1 );

        }
    }

    public struct ValueWithI2 : I2 { }

    [Test]
    public void nullable_value_type_has_the_DirectInterfaces_of_its_value_type()
    {
        var typeCache = new GlobalTypeCache();
        var v = typeCache.Get( typeof( ValueWithI2 ) );
        var nV = typeCache.Get( typeof( ValueWithI2? ) );
        nV.ShouldNotBeSameAs( v );
        nV.NonNullable.ShouldBeSameAs( v );
        var i1 = typeCache.Get( typeof( I1 ) );
        var i2 = typeCache.Get( typeof( I2 ) );

        v.Interfaces.ShouldBe( [i1, i2], ignoreOrder: true );
        v.DirectInterfaces.ShouldBe( [i2] );
        nV.Interfaces.ShouldBe( v.Interfaces );
        nV.DirectInterfaces.ShouldBe( [i2] );
    }

    public interface J2 { }
    public interface I2 : I1 { }
    public interface I3 : I2, J2 { }
    public interface I4 : I3, I2, I1 { }
    public interface J4 : J2 { }
    public interface I5 : I4, J4 { }

    [Test]
    public void from_Interfaces()
    {
        var typeCache = new GlobalTypeCache();
        typeCache.Get( typeof( I5 ) );

        var i1 = typeCache.Find( typeof( I1 ) ).ShouldNotBeNull();
        var j2 = typeCache.Find( typeof( J2 ) ).ShouldNotBeNull();
        var i2 = typeCache.Find( typeof( I2 ) ).ShouldNotBeNull();
        var i3 = typeCache.Find( typeof( I3 ) ).ShouldNotBeNull();
        var i4 = typeCache.Find( typeof( I4 ) ).ShouldNotBeNull();
        var j4 = typeCache.Find( typeof( J4 ) ).ShouldNotBeNull();
        var i5 = typeCache.Find( typeof( I5 ) ).ShouldNotBeNull();

        i5.Interfaces.ShouldBe( [i1, i2, j2, i3, i4, j4], ignoreOrder: true );
        i5.DirectInterfaces.ShouldBe( [i4, j4], ignoreOrder: true );

        i4.Interfaces.ShouldBe( [i1, i2, i3, j2], ignoreOrder: true );
        i4.DirectInterfaces.ShouldBe( [i3], ignoreOrder: true );
    }

}
