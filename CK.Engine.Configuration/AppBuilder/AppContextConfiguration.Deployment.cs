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

public sealed partial class AppContextConfiguration
{
    /// <summary>
    /// The manifest of a host built for a deployment, written in the "$StObjGen" folder of its ".Host" project by the
    /// builder of the deployment (<see cref="WriteDeploymentManifests"/>): CK.AppDeployment adds it to the file that the
    /// publication of the host writes next to its executable. Its extension is not ".json": the Web SDK publishes the json
    /// files of a project.
    /// </summary>
    public const string DeploymentManifestFileName = "$StObjGen/Deployment.manifest";

    static readonly JsonSerializerOptions _manifestOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Writes the manifest (<see cref="DeploymentManifestFileName"/>) of each App built for a deployment in its ".Host"
    /// project: the full name of its party, the deployment (its name, its environment, its <c>"$BASE"</c> chain), the
    /// MSBuild properties of its publication (<see cref="CentralDeploymentSettings.GetPublishProperties"/>), the
    /// names of the secrets that the host needs (the handles of its configuration and of its burnt values), and its values
    /// per deployment (its addresses, its public URLs...: the values covered by the keys per deployment of the App).
    /// The manifest of a fleet (a template App: "InstanceCount", "Instances" or "MaxInstanceCount") lists its instances:
    /// each configured one with its full name, its start arguments and its values per deployment resolved, and the
    /// indexes of the dynamic ones of an Opened fleet. CK.AppDeployment adds the version.
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
            // The required secrets and the optional ones (a name required somewhere is required).
            var secrets = new SortedSet<string>( StringComparer.OrdinalIgnoreCase );
            var optionalSecrets = new SortedSet<string>( StringComparer.OrdinalIgnoreCase );
            CollectSecrets( a.Configuration.AsEnumerable( makePathsRelative: true ), secrets, optionalSecrets );
            var values = PerDeploymentValues( a.Configuration, patterns );
            CollectSecrets( a.ProjectedValues.Concat( a.BinPathConfiguration.CompiledValues ).Select( kv => KeyValuePair.Create( kv.Key, (string?)kv.Value ) ), secrets, optionalSecrets );
            optionalSecrets.ExceptWith( secrets );
            var bases = new JsonArray();
            for( var d = deployment.Base; d != null; d = d.Base ) bases.Add( d.Name );
            var manifest = new JsonObject
            {
                ["FullName"] = a.Configuration["FullName"],
                ["Deployment"] = deployment.Name,
                ["Environment"] = deployment.Environment,
                ["Bases"] = bases,
                ["Publish"] = new JsonObject( deployment.Settings.GetPublishProperties( a.Name )
                                                         .Select( p => new KeyValuePair<string, JsonNode?>( p.Name, p.Value ) ) ),
                ["Secrets"] = new JsonArray( secrets.Select( s => (JsonNode?)s ).ToArray() ),
                ["OptionalSecrets"] = new JsonArray( optionalSecrets.Select( s => (JsonNode?)s ).ToArray() ),
                ["PerDeployment"] = values
            };
            // A fleet: its instances.
            if( !ConfigurationTemplate.TryReadInstances( monitor, a.Configuration, a.Name, out var instances ) )
            {
                success = false;
                continue;
            }
            if( instances != null )
            {
                var configured = new JsonArray();
                foreach( var i in instances )
                {
                    var resolved = ConfigurationTemplate.Instantiate( monitor, a.Configuration, i );
                    if( resolved == null )
                    {
                        success = false;
                        continue;
                    }
                    var (instanceSecrets, instanceOptionalSecrets) = InstanceSecrets( resolved, secrets, optionalSecrets );
                    configured.Add( new JsonObject
                    {
                        ["Name"] = i.Name,
                        ["Index"] = i.Index,
                        ["FullName"] = resolved["FullName"],
                        ["Arguments"] = new JsonArray( (JsonNode?)$"--Instance={i.Name}" ),
                        ["Secrets"] = instanceSecrets,
                        ["OptionalSecrets"] = instanceOptionalSecrets,
                        ["PerDeployment"] = PerDeploymentValues( resolved, patterns )
                    } );
                }
                var o = new JsonObject { ["Configured"] = configured };
                if( instances.MaxInstanceCount is int max && max > instances.Count )
                {
                    o["Dynamic"] = new JsonObject { ["From"] = instances.Count + 1, ["To"] = max };
                }
                manifest["Instances"] = o;
            }
            // The deployment in the "architecture.md" of the host: the same facts as the manifest (never a secret value).
            a.AddArchitecture( "The deployment", DescribeDeployment( manifest ) );
            var file = a.HostProjectPath.Combine( DeploymentManifestFileName );
            try
            {
                Directory.CreateDirectory( file.RemoveLastPart() );
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

        // The "The deployment" section of the architecture of a host, from its manifest.
        static string DescribeDeployment( JsonObject manifest )
        {
            var b = new System.Text.StringBuilder();
            b.Append( "- Deployment: `" ).Append( manifest["Deployment"] ).Append( "` (environment `" ).Append( manifest["Environment"] ).Append( "`)" ).Append( '\n' );
            if( manifest["Bases"] is JsonArray { Count: > 0 } bases ) b.Append( "- Its bases: " ).Append( List( bases ) ).Append( '\n' );
            b.Append( "- Full name: `" ).Append( manifest["FullName"] ).Append( '`' ).Append( '\n' );
            if( manifest["Publish"] is JsonObject { Count: > 0 } publish )
            {
                b.Append( "- Publication: " ).Append( string.Join( ", ", publish.Select( p => $"`{p.Key}={p.Value}`" ) ) ).Append( '\n' );
            }
            b.Append( "- Secrets: " ).Append( manifest["Secrets"] is JsonArray { Count: > 0 } s ? List( s ) : "*(none)*" ).Append( '\n' );
            if( manifest["OptionalSecrets"] is JsonArray { Count: > 0 } o ) b.Append( "- Optional secrets: " ).Append( List( o ) ).Append( '\n' );
            if( manifest["PerDeployment"] is JsonObject { Count: > 0 } values )
            {
                b.Append( '\n' ).Append( "The values per deployment:" ).Append( '\n' ).Append( '\n' )
                 .Append( "| Key | Value |" ).Append( '\n' ).Append( "|---|---|" ).Append( '\n' );
                foreach( var (k, v) in values ) b.Append( "| " ).Append( k ).Append( " | `" ).Append( (v is JsonValue jv && jv.TryGetValue<string>( out var text ) ? text : v?.ToJsonString( _manifestOptions ))?.Replace( "|", "\\|" ) ).Append( "` |" ).Append( '\n' );
            }
            if( manifest["Instances"] is JsonObject instances )
            {
                b.Append( '\n' ).Append( "The instances:" ).Append( '\n' ).Append( '\n' );
                foreach( var i in instances["Configured"]?.AsArray() ?? [] )
                {
                    b.Append( "- `" ).Append( i?["Name"] ).Append( "`: `" ).Append( i?["FullName"] ).Append( '`' ).Append( '\n' );
                }
                if( instances["Dynamic"] is JsonObject d ) b.Append( "- Dynamic: from " ).Append( d["From"] ).Append( " to " ).Append( d["To"] ).Append( '\n' );
            }
            return b.ToString();

            static string List( JsonArray a ) => string.Join( ", ", a.Select( n => $"`{n}`" ) );
        }

        // The names of the secrets, required and optional, of a set of values.
        static void CollectSecrets( IEnumerable<KeyValuePair<string, string?>> values, SortedSet<string> required, SortedSet<string> optional )
        {
            foreach( var (path, value) in values )
            {
                if( value != null && ConfigurationSecret.IsHandlePath( path, out _, out var isOptional ) ) (isOptional ? optional : required).Add( value );
            }
        }

        // The names of the secrets of an instance: its handles resolved, and the names of the App without placeholders
        // (the other ones are its handles).
        static (JsonArray Required, JsonArray Optional) InstanceSecrets( ImmutableConfigurationSection resolved, SortedSet<string> appSecrets, SortedSet<string> appOptionalSecrets )
        {
            var required = new SortedSet<string>( appSecrets.Where( n => !n.Contains( '{' ) ), StringComparer.OrdinalIgnoreCase );
            var optional = new SortedSet<string>( appOptionalSecrets.Where( n => !n.Contains( '{' ) ), StringComparer.OrdinalIgnoreCase );
            CollectSecrets( resolved.AsEnumerable( makePathsRelative: true ), required, optional );
            optional.ExceptWith( required );
            return (new JsonArray( required.Select( n => (JsonNode?)n ).ToArray() ), new JsonArray( optional.Select( n => (JsonNode?)n ).ToArray() ));
        }

        // The values of a configuration covered by the keys per deployment, by their relative path.
        static JsonObject PerDeploymentValues( Microsoft.Extensions.Configuration.IConfiguration configuration, List<ConfigurationPathPattern> patterns )
        {
            var values = new JsonObject();
            foreach( var (path, value) in configuration.AsEnumerable( makePathsRelative: true ).OrderBy( kv => kv.Key, StringComparer.OrdinalIgnoreCase ) )
            {
                if( value != null && patterns.Any( p => p.Covers( path ) ) ) values[path] = value;
            }
            return values;
        }
    }
}
