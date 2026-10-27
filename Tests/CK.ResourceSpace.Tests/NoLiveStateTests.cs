using CK.Core;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using static CK.Testing.MonitorTestHelper;

namespace CK.ResourceSpace.Tests;

[TestFixture]
public class NoLiveStateTests
{
    /// <summary>
    /// <see cref="ResSpaceCollector.NoLiveState"/> disables the Live state even if an "&lt;App&gt;" resources folder exists.
    /// The installation must not write any Live state (the "none" marker must never be used as a path).
    /// </summary>
    [Test]
    public void NoLiveState_disables_the_Live_state_even_with_an_App_resources_folder()
    {
        var root = Path.Combine( Path.GetTempPath(), "CK.ResourceSpace.Tests", Guid.NewGuid().ToString( "N" ) );
        var app = Path.Combine( root, "App" ) + Path.DirectorySeparatorChar;
        var target = Path.Combine( root, "Target" );
        var cwd = Path.Combine( root, "CurrentDirectory" );
        Directory.CreateDirectory( Path.Combine( app, "ts-assets" ) );
        File.WriteAllText( Path.Combine( app, "ts-assets", "a.txt" ), "A" );
        Directory.CreateDirectory( target );
        Directory.CreateDirectory( cwd );
        var savedCurrentDirectory = Environment.CurrentDirectory;
        Environment.CurrentDirectory = cwd;
        try
        {
            var config = new ResSpaceConfiguration();
            config.AppResourcesLocalPath = app;
            config.LiveStatePath = ResSpaceCollector.NoLiveState;

            var collector = config.Build( TestHelper.Monitor ).ShouldNotBeNull();
            var spaceData = new ResSpaceDataBuilder( collector ).Build( TestHelper.Monitor ).ShouldNotBeNull();
            spaceData.CoreData.HasLiveState.ShouldBeFalse();
            spaceData.CoreData.WatchRoot.ShouldBeNull();

            var builder = new ResSpaceBuilder( spaceData );
            builder.RegisterHandler( TestHelper.Monitor,
                                     new AssetsResourceHandler( new FileSystemInstaller( target ),
                                                                spaceData.CoreData.SpaceDataCache,
                                                                "ts-assets" ) ).ShouldBeTrue();
            var space = builder.Build( TestHelper.Monitor ).ShouldNotBeNull();
            space.Install( TestHelper.Monitor ).ShouldBeTrue();

            Directory.GetFileSystemEntries( cwd ).ShouldBeEmpty();
        }
        finally
        {
            Environment.CurrentDirectory = savedCurrentDirectory;
            try { Directory.Delete( root, recursive: true ); }
            catch( Exception ) { /* best effort on a temp folder */ }
        }
    }
}
