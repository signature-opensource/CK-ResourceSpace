using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace CK.Setup;

/// <summary>
/// The settings of a deployment that are not configuration (see <see cref="CentralDeployment"/>): the <c>"Deployment"</c>
/// root key of the central configuration (the defaults of every deployment) or of an overlay (merged like the rest):
/// <code>
/// "Deployment": {
///   // The builders to deploy (their paths in "Builders"): all by default.
///   "Builders": [ "Front/Auth3.Front.Builder", "Admin/Auth3.Admin.Builder" ],
///   // How the hosts are published: framework-dependent by default.
///   "SelfContained": false,
///   "RuntimeIdentifier": "linux-x64"
/// }
/// </code>
/// </summary>
/// <param name="Builders">The paths of the builders to deploy, null for all of them.</param>
/// <param name="SelfContained">Whether the hosts are published with the .NET runtime.</param>
/// <param name="RuntimeIdentifier">The runtime identifier of the published hosts, null for a portable publish.</param>
public sealed record CentralDeploymentSettings( IReadOnlyList<string>? Builders, bool SelfContained, string? RuntimeIdentifier )
{
    /// <summary>
    /// The default settings: all the builders, framework-dependent, portable.
    /// </summary>
    public static readonly CentralDeploymentSettings Default = new( null, false, null );

    /// <summary>
    /// Reads the settings.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="node">The <c>"Deployment"</c> value (null: the defaults).</param>
    /// <param name="configuration">The configuration whose builders can be deployed.</param>
    /// <param name="source">The source, for the errors.</param>
    /// <returns>The settings, or null on error (errors are logged).</returns>
    public static CentralDeploymentSettings? Read( IActivityMonitor monitor, JsonNode? node, CentralConfiguration configuration, string source )
    {
        if( node == null ) return Default;
        if( node is not JsonObject o )
        {
            monitor.Error( $"Invalid \"{CentralDeployment.SettingsKey}\" of {source}: an object is expected (\"Builders\", \"SelfContained\", \"RuntimeIdentifier\")." );
            return null;
        }
        bool success = true;
        List<string>? builders = null;
        bool selfContained = false;
        string? rid = null;
        foreach( var (key, value) in o )
        {
            if( key.Equals( "Builders", StringComparison.OrdinalIgnoreCase ) )
            {
                if( value is not JsonArray a )
                {
                    monitor.Error( $"Invalid \"{CentralDeployment.SettingsKey}:Builders\" of {source}: an array of the paths of the builders is expected." );
                    success = false;
                    continue;
                }
                builders = new List<string>();
                foreach( var item in a )
                {
                    var path = item is JsonValue v && v.TryGetValue<string>( out var s ) ? s.Replace( '\\', '/' ).Trim().Trim( '/' ) : null;
                    var b = path != null ? configuration.Builders.FirstOrDefault( b => b.Path.Equals( path, StringComparison.OrdinalIgnoreCase ) ) : null;
                    if( b == null )
                    {
                        monitor.Error( $"Invalid builder '{item?.ToJsonString()}' in \"{CentralDeployment.SettingsKey}:Builders\" of {source}: the builders are '{configuration.Builders.Select( b => b.Path ).Concatenate( "', '" )}'." );
                        success = false;
                    }
                    else if( !builders.Contains( b.Path ) ) builders.Add( b.Path );
                }
            }
            else if( key.Equals( "SelfContained", StringComparison.OrdinalIgnoreCase ) )
            {
                if( value is not JsonValue v || !v.TryGetValue( out selfContained ) )
                {
                    monitor.Error( $"Invalid \"{CentralDeployment.SettingsKey}:SelfContained\" of {source}: true or false is expected." );
                    success = false;
                }
            }
            else if( key.Equals( "RuntimeIdentifier", StringComparison.OrdinalIgnoreCase ) )
            {
                if( value is not JsonValue v || !v.TryGetValue( out rid ) || string.IsNullOrWhiteSpace( rid ) )
                {
                    monitor.Error( $"Invalid \"{CentralDeployment.SettingsKey}:RuntimeIdentifier\" of {source}: a runtime identifier (\"linux-x64\") is expected." );
                    success = false;
                }
            }
            else
            {
                monitor.Error( $"Unknown key \"{CentralDeployment.SettingsKey}:{key}\" of {source}: \"Builders\", \"SelfContained\" and \"RuntimeIdentifier\" are expected." );
                success = false;
            }
        }
        if( selfContained && rid == null )
        {
            monitor.Error( $"\"{CentralDeployment.SettingsKey}:SelfContained\" of {source} requires a \"RuntimeIdentifier\"." );
            success = false;
        }
        return success ? new CentralDeploymentSettings( builders, selfContained, rid ) : null;
    }
}
