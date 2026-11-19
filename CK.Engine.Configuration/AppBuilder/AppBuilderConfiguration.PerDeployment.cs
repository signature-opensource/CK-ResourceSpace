using CK.Core;
using System.Collections.Generic;
using System.Linq;

namespace CK.Setup;

public sealed partial class AppBuilderConfiguration
{
    /// <summary>
    /// Collects the keys per deployment of an App (see <see cref="CentralDeployment"/>), as patterns relative to the App:
    /// its <see cref="AppConfiguration.PerDeploymentKeys"/> and the declarations of its configurable types
    /// (<c>[ConfigurationPerDeployment( "Party:Urls" )]</c>: the path of each anchor of the kind and of its fallbacks, then
    /// the section of the type, then the rest of the path).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="app">The App (its anchors are declared).</param>
    /// <param name="packageKeys">The declarations of the configurable types of the App.</param>
    /// <param name="keys">The patterns relative to the App, with their source.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public static bool CollectPerDeploymentKeys( IActivityMonitor monitor,
                                                 AppConfiguration app,
                                                 IEnumerable<PackageConfigurationPerDeployment> packageKeys,
                                                 out List<(string Pattern, string Source)> keys )
    {
        keys = app.PerDeploymentKeys.Select( k => (k, $"the key '{k}' of {app}") ).ToList();
        bool success = true;
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
                foreach( var a in app.Anchors.Where( a => a.Kinds.Contains( parts[0] ) ) )
                {
                    foreach( var anchorPath in a.Fallbacks.Prepend( a.Path ) )
                    {
                        // An anchor in an array ("Parties:0") is any item: a pattern has no index.
                        var objectPath = string.Join( ':', anchorPath.Split( ':' ).Select( s => ConfigurationPathPattern.IsIndex( s ) ? "[*]" : s ) );
                        var k = (objectPath.Length == 0 ? $"{section}:{parts[1]}" : $"{objectPath}:{section}:{parts[1]}", source);
                        if( !keys.Contains( k ) ) keys.Add( k );
                    }
                }
            }
        }
        return success;
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
