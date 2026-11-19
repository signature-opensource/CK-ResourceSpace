using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CK.Setup;

/// <summary>
/// An entry of a <c>"$LOCK"</c> of a "ckonf.json": <c>"$LOCK": { "Pattern": "Level" }</c> in any object. The builder
/// removes them before anything reads the configuration (they are a build input: never projected).
/// </summary>
public sealed class ConfigurationLockEntry
{
    /// <summary>
    /// The reserved key.
    /// </summary>
    public const string LockKey = "$LOCK";

    /// <summary>
    /// Initializes a new entry.
    /// </summary>
    /// <param name="objectPath">The configuration path of the object that holds the "$LOCK".</param>
    /// <param name="pattern">The pattern, relative to this object.</param>
    /// <param name="level">The level text.</param>
    public ConfigurationLockEntry( string objectPath, string pattern, string? level )
    {
        ObjectPath = objectPath;
        Pattern = pattern;
        Level = level;
    }

    /// <summary>
    /// Gets the configuration path of the object that holds the "$LOCK" ("ckonf:App:Parties:0").
    /// </summary>
    public string ObjectPath { get; }

    /// <summary>
    /// Gets the pattern (see <see cref="ConfigurationPathPattern"/>), relative to <see cref="ObjectPath"/>.
    /// </summary>
    public string Pattern { get; }

    /// <summary>
    /// Gets the level as written: "Compile", "Init" or "Run" (case insensitive). Null when it is not a string.
    /// </summary>
    public string? Level { get; }

    /// <summary>
    /// Gets the path of this entry, for the errors.
    /// </summary>
    public string SourcePath => $"{ObjectPath}:{LockKey}:{Pattern}";

    /// <summary>
    /// Tries to parse the <see cref="Level"/>.
    /// </summary>
    /// <param name="level">The level on success.</param>
    /// <returns>True on success.</returns>
    public bool TryGetLevel( out ConfigurationLevel level )
    {
        level = default;
        return Level != null
               && !int.TryParse( Level, out _ )
               && Enum.TryParse( Level, ignoreCase: true, out level )
               && Enum.IsDefined( level );
    }

    /// <inheritdoc />
    public override string ToString() => $"\"{SourcePath}\": \"{Level}\"";

    /// <summary>
    /// Extracts and removes the "$LOCK" of a JSON (recursively, the items of the arrays included).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="node">The JSON object.</param>
    /// <param name="path">Its configuration path.</param>
    /// <param name="locks">The collector.</param>
    /// <returns>True on success, false if a "$LOCK" is not an object of strings.</returns>
    public static bool Extract( IActivityMonitor monitor, JsonObject node, string path, List<ConfigurationLockEntry> locks )
    {
        bool success = true;
        foreach( var key in node.Select( kv => kv.Key ).ToList() )
        {
            var value = node[key];
            if( key.Equals( LockKey, StringComparison.OrdinalIgnoreCase ) )
            {
                node.Remove( key );
                if( value is not JsonObject l )
                {
                    monitor.Error( $"\"{path}:{key}\" must be an object: {{ \"Pattern\": \"Compile\" }} (the levels are Compile, Init and Run)." );
                    success = false;
                    continue;
                }
                foreach( var (pattern, level) in l )
                {
                    var text = level is JsonValue v && v.GetValueKind() == JsonValueKind.String ? v.GetValue<string>() : null;
                    locks.Add( new ConfigurationLockEntry( path, pattern, text ) );
                }
                continue;
            }
            success &= Extract( monitor, value, $"{path}:{key}", locks );
        }
        return success;

        static bool Extract( IActivityMonitor monitor, JsonNode? n, string path, List<ConfigurationLockEntry> locks )
        {
            if( n is JsonObject o ) return ConfigurationLockEntry.Extract( monitor, o, path, locks );
            if( n is JsonArray a )
            {
                bool success = true;
                for( int i = 0; i < a.Count; ++i ) success &= Extract( monitor, a[i], $"{path}:{i}", locks );
                return success;
            }
            return true;
        }
    }
}

/// <summary>
/// A restriction declared by a package (<see cref="ConfigurationLockAttribute"/> on a configurable type of an App's set):
/// the builder gives them to <see cref="AppBuilderConfiguration.ApplyLocks"/>.
/// </summary>
/// <param name="TypeName">The name of the type that declares it (for the errors).</param>
/// <param name="SectionName">The name of the section of the type in a party (its configurable name).</param>
/// <param name="Attribute">The attribute.</param>
public sealed record PackageConfigurationLock( string TypeName, string SectionName, ConfigurationLockAttribute Attribute );
