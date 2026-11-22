using CK.Core;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CK.Setup;

public sealed partial class AppBuilderConfiguration
{
    /// <summary>
    /// Collects the keys per deployment of an App (see <see cref="CentralDeployment"/>), as patterns relative to the App:
    /// <list type="bullet">
    ///     <item>its <see cref="AppConfiguration.PerDeploymentKeys"/>;</item>
    ///     <item>the size of a fleet: the "InstanceCount", "Instances" and "MaxInstanceCount" of an App that is a
    ///     template (see <see cref="ConfigurationTemplate.IsTemplate"/>);</item>
    ///     <item>the declarations of its configurable types (<c>[ConfigurationPerDeployment( "Party:Urls" )]</c>: the path
    ///     of each anchor of the kind and of its fallbacks, then the section of the type, then the rest of the path);</item>
    ///     <item>its <see cref="AppConfiguration.PerDeploymentPartyKeys"/> and the <c>[assembly: ConfigurationPerDeployment( "Remote:Address" )]</c>
    ///     of the assemblies of the contributors (the path of each anchor of the kind and of its fallbacks, then the path of
    ///     the property).</item>
    /// </list>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="app">The App (its anchors are declared).</param>
    /// <param name="packageKeys">The declarations of the configurable types of the App.</param>
    /// <param name="contributors">The App builder contributors (their assemblies declare the keys of the parties).</param>
    /// <param name="keys">The patterns relative to the App, with their source.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public static bool CollectPerDeploymentKeys( IActivityMonitor monitor,
                                                 AppConfiguration app,
                                                 IEnumerable<PackageConfigurationPerDeployment> packageKeys,
                                                 IEnumerable<IAppBuilderContributor> contributors,
                                                 out List<(string Pattern, string Source)> keys )
    {
        keys = app.PerDeploymentKeys.Select( k => (k, $"the key '{k}' of {app}") ).ToList();
        // The size of a fleet is the one of a deployment (the remote templates of the links to the fleet copy it from
        // the same merged configuration).
        if( ConfigurationTemplate.IsTemplate( app.Configuration ) )
        {
            foreach( var k in (string[])[ConfigurationTemplate.InstanceCountKey, ConfigurationTemplate.InstancesKey, ConfigurationTemplate.MaxInstanceCountKey] )
            {
                if( !keys.Any( e => e.Pattern == k ) ) keys.Add( (k, $"the size of the fleet {app}") );
            }
        }
        foreach( var (kind, path) in app.PerDeploymentPartyKeys )
        {
            AddAnchorKeys( app, keys, kind, path, $"the key '{path}' of the parties '{kind}' of {app}" );
        }
        bool success = true;
        foreach( var assembly in contributors.Select( c => c.GetType().Assembly ).Distinct() )
        {
            foreach( var a in assembly.GetCustomAttributes<ConfigurationPerDeploymentAttribute>() )
            {
                var source = $"[assembly: ConfigurationPerDeployment( \"{a.Path}\" )] of '{assembly.GetName().Name}'";
                var parts = a.Path?.Split( ':', 2 );
                if( parts == null || parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0 )
                {
                    monitor.Error( $"Invalid {source}: its path is the kind of the parties, then the path in the party (\"Remote:Address\")." );
                    success = false;
                    continue;
                }
                AddAnchorKeys( app, keys, parts[0], parts[1], source );
            }
        }
        foreach( var p in packageKeys )
        {
            var source = $"[ConfigurationPerDeployment( \"{p.Path}\" )] of '{p.TypeName}'";
            var parts = p.Path?.Split( ':', 2 );
            if( parts == null || parts.Length != 2 || parts[0].Length == 0 )
            {
                monitor.Error( $"Invalid {source}: its path is the kind of the parties, then the path in the section of the type (\"Party:Urls\")." );
                success = false;
                continue;
            }
            foreach( var section in p.Sections )
            {
                AddAnchorKeys( app, keys, parts[0], $"{section}:{parts[1]}", source );
            }
        }
        return success;

        // The path in each anchor of the kind and in its fallbacks.
        static void AddAnchorKeys( AppConfiguration app, List<(string Pattern, string Source)> keys, string kind, string path, string source )
        {
            foreach( var a in app.Anchors.Where( a => a.Kinds.Contains( kind ) ) )
            {
                foreach( var anchorPath in a.Fallbacks.Prepend( a.Path ) )
                {
                    // An anchor in an array ("Parties:0") is any item: a pattern has no index.
                    var objectPath = string.Join( ':', anchorPath.Split( ':' ).Select( s => ConfigurationPathPattern.IsIndex( s ) ? "[*]" : s ) );
                    var k = (objectPath.Length == 0 ? path : $"{objectPath}:{path}", source);
                    if( !keys.Contains( k ) ) keys.Add( k );
                }
            }
        }
    }
}

/// <summary>
/// A declaration of values per deployment by a configurable type of an App (a <c>[ConfigurationPerDeployment]</c>):
/// the builder gives them to <see cref="AppBuilderConfiguration.CollectPerDeploymentKeys"/>.
/// </summary>
/// <param name="TypeName">The name of the type that declares it (for the messages).</param>
/// <param name="Sections">
/// The paths of the sections of the type relative to an anchor: its configurable name for a feature, the section in
/// each of its owners for an option (see <see cref="IAppBuilderContributor.ResolveSections"/>).
/// </param>
/// <param name="Path">The path of the attribute: the kind of the parties, then the path in the section of the type.</param>
public sealed record PackageConfigurationPerDeployment( string TypeName, IReadOnlyList<string> Sections, string Path );
