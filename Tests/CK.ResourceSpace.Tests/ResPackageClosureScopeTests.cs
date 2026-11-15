using CK.Core;
using CK.Engine.TypeCollector;
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
                                                 checkSingleUniqueness: false )
                                        .ShouldNotBeNull();
        closure.Select( t => t.Type.Name ).Order().ShouldBe( ["Hosting", "NgBuild", "ViteBuild"] );

        // Without root implementation, the only candidate is still required (else it is ambiguous).
        ResPackageClosure.Compute( TestHelper.Monitor,
                                   cache,
                                   new[] { typeof( NgBuild ), typeof( Hosting ) }.Select( cache.Get ),
                                   t => t.Type == typeof( Hosting ),
                                   checkSingleUniqueness: false )
                         .ShouldNotBeNull()
                         .Select( t => t.Type.Name ).Order().ShouldBe( ["Hosting", "NgBuild"] );
    }
}
