using CK.Core;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CK.Setup;

public sealed partial class AppBuilderConfiguration
{
    /// <summary>
    /// The manifest of a host built for a deployment, written in its ".Host" project by the builder of the deployment
    /// (and copied in the published host): <see cref="WriteDeploymentManifests"/>.
    /// </summary>
    public const string DeploymentManifestFileName = "Deployment.json";

    static readonly JsonSerializerOptions _manifestOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Writes the manifest (<see cref="DeploymentManifestFileName"/>) of each App built for a deployment in its ".Host"
    /// project: the full name of its party, the deployment (its name, its environment, its <c>"$BASE"</c> chain), the
    /// names of the secrets that the host needs (the handles of its configuration and of its burnt values), and its values
    /// per deployment (its addresses, its public URLs...: the values covered by the keys per deployment of the App).
    /// CK.AppDeployment adds the version.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="deployment">The deployment.</param>
    /// <param name="appKeys">The keys per deployment of each App, by App name (see <see cref="CollectPerDeploymentKeys"/>).</param>
    /// <returns>True on success, false on error.</returns>
    public bool WriteDeploymentManifests( IActivityMonitor monitor,
                                          CentralDeployment deployment,
                                          IReadOnlyDictionary<string, IReadOnlyList<(string Pattern, string Source)>> appKeys )
    {
        bool success = true;
        foreach( var a in Apps )
        {
            var patterns = new List<ConfigurationPathPattern>();
            foreach( var (text, _) in appKeys.GetValueOrDefault( a.Name ) ?? [] )
            {
                if( ConfigurationPathPattern.TryParse( text, out var p, out _ ) ) patterns.Add( p );
            }
            var secrets = new SortedSet<string>( StringComparer.OrdinalIgnoreCase );
            var values = new JsonObject();
            var handleSuffix = ":" + ConfigurationSecret.HandleKey;
            foreach( var (path, value) in a.Configuration.AsEnumerable( makePathsRelative: true ).OrderBy( kv => kv.Key, StringComparer.OrdinalIgnoreCase ) )
            {
                if( value == null ) continue;
                if( path.EndsWith( handleSuffix, StringComparison.OrdinalIgnoreCase ) ) secrets.Add( value );
                if( patterns.Any( p => p.Covers( path ) ) ) values[path] = value;
            }
            foreach( var (path, value) in a.ProjectedValues.Concat( a.BinPathConfiguration.CompiledValues ) )
            {
                if( path.EndsWith( handleSuffix, StringComparison.OrdinalIgnoreCase ) ) secrets.Add( value );
            }
            var bases = new JsonArray();
            for( var d = deployment.Base; d != null; d = d.Base ) bases.Add( d.Name );
            var manifest = new JsonObject
            {
                ["FullName"] = a.Configuration["FullName"],
                ["Deployment"] = deployment.Name,
                ["Environment"] = deployment.Environment,
                ["Bases"] = bases,
                ["Secrets"] = new JsonArray( secrets.Select( s => (JsonNode?)s ).ToArray() ),
                ["PerDeployment"] = values
            };
            var file = a.HostProjectPath.AppendPart( DeploymentManifestFileName );
            try
            {
                File.WriteAllText( file, manifest.ToJsonString( _manifestOptions ) + Environment.NewLine );
                monitor.Info( $"'{file}' written: {secrets.Count} secret(s), {values.Count} value(s) per deployment." );
            }
            catch( Exception ex )
            {
                monitor.Error( $"While writing '{file}'.", ex );
                success = false;
            }
        }
        return success;
    }
}
