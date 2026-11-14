using CK.Core;
using CK.Setup;
using Shouldly;
using NUnit.Framework;
using System;
using System.Linq;
using System.Reflection;
using static CK.Testing.MonitorTestHelper;

namespace CK.Engine.TypeCollector.Tests;

[TestFixture]
public class AssemblyAndTypeCacheTests
{
    [Test]
    public void TypeCache_on_generics()
    {
        var config = new EngineConfiguration();
        config.FirstBinPath.Path = TestHelper.BinFolder;
        config.FirstBinPath.Assemblies.Add( "CK.Engine.TypeCollector.Tests" );

        var typeCache = AssemblyCache.Run( TestHelper.Monitor, config ).TypeCache;

        var v8 = typeCache.Get( typeof( ValueTuple<,,,,,,,> ) );
        v8.CSharpName.ShouldBe( "System.ValueTuple<T1,T2,T3,T4,T5,T6,T7,TRest>" );
        var gArgs = v8.GenericArguments;
        gArgs.Select( p => p.ToString() ).Concatenate().ShouldBe( "T1, T2, T3, T4, T5, T6, T7, TRest" );

        var oneV8 = (1, "string", 3, (object?)null, 5, typeof( void ), 7, "8");
        var iV8 = typeCache.Get( oneV8.GetType() );
        iV8.CSharpName.ShouldBe( "(int,string,int,object,int,System.Type,int,string)" );
        gArgs = iV8.GenericArguments;
        gArgs.Select( p => p.ToString() ).Concatenate().ShouldBe( "int, string, int, object, int, System.Type, int, (string)" );
    }

    /// <summary>
    /// This browses a big set of types from mscorlib, CK.Core.ActivityMonitor and CK.Core.ActivityMonitor.
    /// Among them (especially mscorlib), all kind of types can be found.
    /// We use the ToString() method of the DeclaredMethodInfo to walk across the types and check that
    /// the CK.Core.TypeExtensions.ToCSharpName can also handle everything.
    /// </summary>
    [Test]
    public void TypeCache_can_handle_all_types()
    {
        var config = new EngineConfiguration();
        config.FirstBinPath.Path = TestHelper.BinFolder;
        config.FirstBinPath.Assemblies.Add( "CK.Engine.TypeCollector.Tests" );

        var typeCache = AssemblyCache.Run( TestHelper.Monitor, config ).TypeCache;

        DumpTypes( TestHelper.Monitor, typeCache, typeof( CK.Core.ActivityMonitorSimpleSenderExtension ).Assembly );
        DumpTypes( TestHelper.Monitor, typeCache, typeof( CK.Core.ActivityMonitor ).Assembly );
        DumpTypes( TestHelper.Monitor, typeCache, typeof( string ).Assembly );

        static void DumpTypes( IActivityMonitor monitor, GlobalTypeCache typeCache, Assembly a )
        {
            using( monitor.OpenInfo( a.ToString() ) )
            {
                foreach( var t in a.GetTypes() )
                {
                    var cT = typeCache.Get( t );
                    using( monitor.OpenInfo( cT.ToString() ) )
                    {
                        cT.Type.ShouldBe( t );
                        foreach( var m in cT.DeclaredMembers )
                        {
                            m.ToString().ShouldNotBeNull();
                        }
                        cT.GenericArguments.IsDefault.ShouldBeFalse();
                    }
                }
            }
        }
    }

    // See https://stackoverflow.com/a/6704993/190380
    class Outer<TOuter>
    {
        public class XClass<T, U> { }
    }

    class SemiOpen<T> : Outer<T>.XClass<T,int> { }

    [Test]
    public void TypeCache_on_open_constructed_generic()
    {
        var typeCache = new GlobalTypeCache();
        {
            Type t = typeof( SemiOpen<> );
            Type? tBase = t.BaseType;
            Throw.DebugAssert( tBase != null );

            var cT = typeCache.Get( t );
            var cBase = typeCache.Get( tBase );
            Throw.DebugAssert( cT.BaseType != null );
            cT.BaseType.ShouldBeSameAs( cBase );

            cBase.IsTypeDefinition.ShouldBeFalse();
            cBase.IsTypeDefinition.ShouldBe( tBase.IsTypeDefinition );
            cBase.IsGenericType.ShouldBeTrue();
            cBase.IsGenericType.ShouldBe( tBase.IsGenericType );
            cBase.ToString().ShouldBe( "CK.Engine.TypeCollector.Tests.AssemblyAndTypeCacheTests.Outer<T>.XClass<T,int>" );
            cBase.GenericArguments.Count().ShouldBe( 3 );
            cBase.GenericArguments[0].CSharpName.ShouldBe( "T" );
            cBase.GenericArguments[0].DeclaringType.ShouldBe( cT );
            cBase.GenericArguments[1].ShouldBeSameAs( cBase.GenericArguments[0] );
            cBase.GenericArguments[2].ShouldBeSameAs( typeCache.Get( typeof(int) ) );

            Throw.DebugAssert( cBase.GenericTypeDefinition != null );
            cBase.GenericTypeDefinition.ToString().ShouldBe( "CK.Engine.TypeCollector.Tests.AssemblyAndTypeCacheTests.Outer<TOuter>.XClass<T,U>" );
        }
    }

    [AttributeUsage( AttributeTargets.GenericParameter )]
    class MyAttribute : Attribute { }

    interface ISome<[My]T> { }

    [Test]
    public void TypeCache_handles_attributes_on_generic_parameters()
    {
        var typeCache = new GlobalTypeCache();
        var t = typeCache.Get( typeof(ISome<>) );
        var tParam = t.GenericArguments[0];
        tParam.AttributesData.ShouldNotBeEmpty();
        tParam.AttributesData[0].AttributeType.ShouldBe( typeof( MyAttribute ) );
        tParam.RawAttributes.ShouldNotBeEmpty();
        tParam.RawAttributes[0].ShouldBeOfType<MyAttribute>();

    }

}
