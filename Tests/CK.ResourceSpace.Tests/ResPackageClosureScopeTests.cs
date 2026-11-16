using CK.Core;
using CK.TypeCache;
using NUnit.Framework;
using Shouldly;
using System;
using System.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CK.ResourceSpace.Tests;

[TestFixture]
public class ResPackageClosureScopeTests
{
    [AttributeUsage( AttributeTargets.Interface )]
    public sealed class SingleScopeAttribute : Attribute, ISingleMappingAttribute { }

    [SingleScope]
    public interface IBuild : IResourceGroup { }

    public sealed class NgBuild : IBuild { }

    public sealed class ViteBuild : IBuild { }

    [Requires<IBuild>]
    public sealed class Hosting : IResourceGroup { }

    static readonly Type[] _candidates = [typeof( NgBuild ), typeof( ViteBuild ), typeof( Hosting )];

    [SingleScope]
    public interface IEndpoint : IResourceGroup { }

    public sealed class Endpoint1 : IEndpoint { }

    public sealed class Endpoint2 : IEndpoint { }

    [Test]
    public void the_caller_decides_which_single_abstractions_are_unique()
    {
        var cache = new GlobalTypeCache();
        Type[] candidates = [.. _candidates, typeof( Endpoint1 ), typeof( Endpoint2 )];
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            // IBuild is not unique here (one per client), IEndpoint is.
            ResPackageClosure.Compute( TestHelper.Monitor,
                                       cache,
                                       candidates.Select( cache.Get ),
                                       _ => true,
                                       isUnique: a => a.Type != typeof( IBuild ) )
                             .ShouldBeNull();
            texts.ShouldContain( t => t.Contains( "IEndpoint' is a single abstraction but more than one kept package implement it" ) );
            texts.ShouldNotContain( t => t.Contains( "IBuild" ) );
        }
    }

    [Test]
    public void in_one_scope_a_single_abstraction_has_at_most_one_implementation()
    {
        var cache = new GlobalTypeCache();
        using( TestHelper.Monitor.CollectTexts( out var texts ) )
        {
            ResPackageClosure.Compute( TestHelper.Monitor,
                                       cache,
                                       _candidates.Select( cache.Get ),
                                       _ => true )
                             .ShouldBeNull();
            texts.ShouldContain( t => t.Contains( "Ambiguous single abstraction" ) );
        }
    }

    [Test]
    public void across_scopes_all_the_root_implementations_are_kept()
    {
        var cache = new GlobalTypeCache();
        var closure = ResPackageClosure.Compute( TestHelper.Monitor,
                                                 cache,
                                                 _candidates.Select( cache.Get ),
                                                 t => t.Type == typeof( Hosting ) || t.Type == typeof( NgBuild ) || t.Type == typeof( ViteBuild ),
                                                 isUnique: _ => false )
                                        .ShouldNotBeNull();
        closure.Select( t => t.Type.Name ).Order().ShouldBe( ["Hosting", "NgBuild", "ViteBuild"] );

        // Without root implementation, the only candidate is still required (else it is ambiguous).
        ResPackageClosure.Compute( TestHelper.Monitor,
                                   cache,
                                   new[] { typeof( NgBuild ), typeof( Hosting ) }.Select( cache.Get ),
                                   t => t.Type == typeof( Hosting ),
                                   isUnique: _ => false )
                         .ShouldNotBeNull()
                         .Select( t => t.Type.Name ).Order().ShouldBe( ["Hosting", "NgBuild"] );
    }
}
