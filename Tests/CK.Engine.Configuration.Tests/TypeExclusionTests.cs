using CK.Setup;
using NUnit.Framework;
using Shouldly;
using System;

namespace CK.Engine.Configuration.Tests;

[TestFixture]
public class TypeExclusionTests
{
    public class Base { }
    public class Specialized : Base { }
    public interface IService { }
    public interface IExtended : IService { }
    public class Impl : IExtended { }
    public interface IRepository<T> { }
    public class UserRepository : IRepository<string> { }
    public class GenericBase<T> { }
    public class FromGenericBase : GenericBase<int> { }
    public struct Value : IService { }

    [TestCase( typeof( Base ), typeof( Base ), true )]
    [TestCase( typeof( Base ), typeof( Specialized ), true )]
    [TestCase( typeof( Specialized ), typeof( Base ), false )]
    [TestCase( typeof( IService ), typeof( IExtended ), true )]
    [TestCase( typeof( IService ), typeof( Impl ), true )]
    [TestCase( typeof( IExtended ), typeof( IService ), false )]
    [TestCase( typeof( IRepository<> ), typeof( IRepository<int> ), true )]
    [TestCase( typeof( IRepository<> ), typeof( UserRepository ), true )]
    [TestCase( typeof( IRepository<int> ), typeof( UserRepository ), false )]
    [TestCase( typeof( GenericBase<> ), typeof( FromGenericBase ), true )]
    [TestCase( typeof( GenericBase<> ), typeof( GenericBase<string> ), true )]
    [TestCase( typeof( GenericBase<> ), typeof( Base ), false )]
    [TestCase( typeof( IService ), typeof( Value? ), true )]
    [TestCase( typeof( Value ), typeof( Value? ), true )]
    public void exclusion_follows_the_Liskov_substitution_principle( Type excluded, Type type, bool expected )
    {
        excluded.Excludes( type ).ShouldBe( expected );
    }

    [Test]
    public void FindExcluder_returns_the_excluding_type()
    {
        Type[] excluded = [typeof( Specialized ), typeof( IService )];
        excluded.FindExcluder( typeof( Impl ) ).ShouldBe( typeof( IService ) );
        excluded.FindExcluder( typeof( Base ) ).ShouldBeNull();
    }
}
