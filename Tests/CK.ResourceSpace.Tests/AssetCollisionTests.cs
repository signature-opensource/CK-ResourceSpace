using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using static CK.Testing.MonitorTestHelper;

namespace CK.ResourceSpace.Tests;

/// <summary>
/// End-to-end behaviour of the assets pipeline when two packages claim the same target path,
/// observed on the installed folder. The handler and root folder name are the ones
/// <c>CK.TypeScript.Engine.TypeScriptContext</c> registers: <c>AssetsResourceHandler( installer, cache, "ts-assets" )</c>.
/// <para>
/// The two packages are identical in both fixtures. Only their relationship changes, and that is what
/// decides which merge operation sees the collision:
/// <list type="bullet">
///     <item>a dependency relation goes through <c>ResourceAssetDefinitionSet.Combine</c>, which reports it;</item>
///     <item>siblings go through <c>FinalResourceAssetSet.Aggregate</c>, which does not.</item>
///     </list>
/// See <c>CK.EmbeddedResources.Assets.Tests.AggregateTests</c> for the unit-level view of the same defect.
/// </para>
/// <para>
/// IMPORTANT - this project consumes <c>CK.EmbeddedResources.Assets</c> as a NuGet package, not as a
/// project reference. The fix in CK-EmbeddedResources (<c>Aggregate</c> now calls
/// <c>f1.AddAmbiguity( f2 )</c>) is therefore NOT visible here until that package is repacked and this
/// project's version is bumped. When it is, the two fixtures below swap: delete the characterization
/// test and enable the one that is currently ignored.
/// </para>
/// </summary>
[TestFixture]
public class AssetCollisionTests
{
    [Test]
    public void Sibling_packages_claiming_the_same_target_path_should_be_refused()
    {
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            using var t = new Run( siblings: true );

            t.Handler.FinalAssets.ShouldBeNull();
            t.SpaceBuilt.ShouldBeFalse( "A handler that fails to initialize aborts ResSpaceBuilder.Build." );
            t.Installed.ShouldBeFalse();
            t.InstalledFiles.ShouldBeEmpty();
            logs.ShouldContain( x => x.StartsWith( "Ambiguities detected in assets:" ) );
        }
    }

    /// <summary>
    /// The control: same two packages, same two files, but P2 requires P1. The guard works here.
    /// </summary>
    [Test]
    public void Dependent_packages_claiming_the_same_target_path_are_refused()
    {
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            using var t = new Run( siblings: false );

            t.Handler.FinalAssets.ShouldBeNull();
            t.SpaceBuilt.ShouldBeFalse( "A handler that fails to initialize aborts ResSpaceBuilder.Build." );
            t.Installed.ShouldBeFalse();
            t.InstalledFiles.ShouldBeEmpty();
            logs.ShouldContain( x => x.Contains( "An explicit override declaration" ) );
            logs.ShouldContain( x => x.StartsWith( "Ambiguities detected in assets:" ) );
        }
    }

    /// <summary>
    /// Builds a ResourceSpace with the two local packages <see cref="P1.Package"/> and
    /// <see cref="P2.Package"/>, then installs it into a temporary folder.
    /// <para>
    /// Both embed "Res/ts-assets/logo.png" and map it to "logos" in their own "assets.jsonc", so both
    /// claim 'logos/logo.png'. Neither declares an override.
    /// </para>
    /// </summary>
    sealed class Run : IDisposable
    {
        public readonly string TargetPath;
        public readonly AssetsResourceHandler Handler;
        /// <summary>
        /// False when <see cref="ResSpaceBuilder.Build"/> returned null: a handler that fails to
        /// initialize aborts the build, before any install.
        /// </summary>
        public readonly bool SpaceBuilt;
        public readonly bool Installed;

        public Run( bool siblings )
        {
            TargetPath = Path.Combine( Path.GetTempPath(), "CK.ResourceSpace.Tests", Guid.NewGuid().ToString( "N" ) );
            Directory.CreateDirectory( TargetPath );

            var config = new ResSpaceConfiguration();
            config.LiveStatePath = ResSpaceCollector.NoLiveState;

            config.RegisterPackage( TestHelper.Monitor, typeof( P1.Package ) ).ShouldNotBeNull();
            var p2 = config.RegisterPackage( TestHelper.Monitor, typeof( P2.Package ) ).ShouldNotBeNull();
            if( !siblings ) p2.Requires.Add( typeof( P1.Package ) );

            var collector = config.Build( TestHelper.Monitor ).ShouldNotBeNull();
            var spaceData = new ResSpaceDataBuilder( collector ).Build( TestHelper.Monitor ).ShouldNotBeNull();

            var builder = new ResSpaceBuilder( spaceData );
            Handler = new AssetsResourceHandler( new FileSystemInstaller( TargetPath ),
                                                 spaceData.CoreData.SpaceDataCache,
                                                 "ts-assets" );
            builder.RegisterHandler( TestHelper.Monitor, Handler ).ShouldBeTrue();

            var space = builder.Build( TestHelper.Monitor );
            SpaceBuilt = space != null;
            Installed = space != null && space.Install( TestHelper.Monitor );
        }

        public string[] InstalledFiles => Directory.Exists( TargetPath )
                                            ? Directory.GetFiles( TargetPath, "*", SearchOption.AllDirectories )
                                            : [];

        public void Dispose()
        {
            try { Directory.Delete( TargetPath, recursive: true ); }
            catch( Exception ) { /* best effort on a temp folder */ }
        }
    }
}
