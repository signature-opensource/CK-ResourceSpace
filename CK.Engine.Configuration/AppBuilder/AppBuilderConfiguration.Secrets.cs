using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CK.Setup;

public sealed partial class AppBuilderConfiguration
{
    /// <summary>
    /// Checks the secrets (see <see cref="ConfigurationSecret"/>) of the Apps and of the Builder aspects: a handle
    /// (<c>{ "$SECRET": "&lt;Name&gt;" }</c>) must be alone in its object and have a valid name; a plain value where a
    /// secret is declared (<see cref="AppConfiguration.AddSecretKey(string)"/>, <see cref="ConfigurationSecretAttribute"/>)
    /// is reported: the development configuration accepts it, a handle keeps it out of the files.
    /// Called once the contributors have contributed (they declared the anchors and the secret keys).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="packageSecrets">The secrets declared by the configurable types of each App (its type set).</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public bool CheckSecrets( IActivityMonitor monitor, Func<AppConfiguration, IEnumerable<PackageConfigurationSecret>>? packageSecrets = null )
    {
        bool success = _aspects.Section == null || CheckHandles( monitor, _aspects.Section );
        foreach( var a in _apps )
        {
            success &= CheckSecrets( monitor, a, packageSecrets?.Invoke( a ) ?? [] );
        }
        return success;
    }

    /// <summary>
    /// Checks the secrets of one App: see <see cref="CheckSecrets(IActivityMonitor, Func{AppConfiguration, IEnumerable{PackageConfigurationSecret}}?)"/>.
    /// This is public for the tests of the contributors.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="app">The App.</param>
    /// <param name="packageSecrets">The secrets declared by the configurable types of the App.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public static bool CheckSecrets( IActivityMonitor monitor, AppConfiguration app, IEnumerable<PackageConfigurationSecret> packageSecrets )
    {
        if( !CheckHandles( monitor, app.Configuration ) ) return false;
        bool success = true;
        var root = app.Configuration;
        var reported = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        foreach( var key in app.SecretKeys )
        {
            if( !ConfigurationPathPattern.TryParse( key, out var pattern, out var error ) )
            {
                monitor.Error( $"Invalid secret key '{key}' of {app}: {error}." );
                success = false;
                continue;
            }
            ReportPlainValues( monitor, root, "", pattern, $"the secret key '{key}'", reported );
        }
        foreach( var p in packageSecrets )
        {
            var source = $"[ConfigurationSecret( \"{p.Attribute.Path}\" )] of '{p.TypeName}'";
            var parts = p.Attribute.Path?.Split( ':', 2 );
            if( parts == null || parts.Length != 2 || parts[0].Length == 0 )
            {
                monitor.Error( $"Invalid {source}: its path is the kind of the parties, then the path in the section of the type (\"Party:Oidc:*:ClientSecret\")." );
                success = false;
                continue;
            }
            foreach( var section in p.Sections )
            {
                if( !ConfigurationPathPattern.TryParse( $"{section}:{parts[1]}", out var pattern, out var error ) )
                {
                    monitor.Error( $"Invalid {source}: {error}." );
                    success = false;
                    break;
                }
                // The values of the parties of this kind: their own ones and the ones of their "Defaults".
                foreach( var a in app.Anchors.Where( a => a.Kinds.Contains( parts[0] ) ) )
                {
                    foreach( var objectPath in a.Fallbacks.Prepend( a.Path ) )
                    {
                        ReportPlainValues( monitor, root, objectPath, pattern, source, reported );
                    }
                }
            }
        }
        return success;
    }

    static void ReportPlainValues( IActivityMonitor monitor,
                                   ImmutableConfigurationSection root,
                                   string objectPath,
                                   ConfigurationPathPattern pattern,
                                   string source,
                                   HashSet<string> reported )
    {
        var s = objectPath.Length == 0 ? root : root.TryGetSection( objectPath );
        if( s == null ) return;
        foreach( var (path, value) in PlainLeaves( s, "" ) )
        {
            if( !pattern.Covers( path ) ) continue;
            var full = objectPath.Length == 0 ? $"{root.Path}:{path}" : $"{root.Path}:{objectPath}:{path}";
            if( reported.Add( full ) )
            {
                // The development configuration accepts it (a known test secret, an Integrated Security connection string).
                monitor.Info( $"\"{full}\" is a secret ({source}) whose value is in the configuration: a handle keeps it out of the files ({{ \"{ConfigurationSecret.HandleKey}\": \"<Name>\" }})." );
            }
        }

        static IEnumerable<(string Path, string Value)> PlainLeaves( ImmutableConfigurationSection s, string prefix )
        {
            foreach( var c in s.GetChildren() )
            {
                var path = prefix.Length == 0 ? c.Key : $"{prefix}:{c.Key}";
                if( c.Value != null && !c.Key.Equals( ConfigurationSecret.HandleKey, StringComparison.OrdinalIgnoreCase ) )
                {
                    yield return (path, c.Value);
                }
                foreach( var l in PlainLeaves( c, path ) ) yield return l;
            }
        }
    }

    static bool CheckHandles( IActivityMonitor monitor, ImmutableConfigurationSection section )
    {
        bool success = true;
        foreach( var c in section.GetChildren() )
        {
            if( c.Key.Equals( ConfigurationSecret.HandleKey, StringComparison.OrdinalIgnoreCase ) )
            {
                if( !ConfigurationSecret.IsValidName( c.Value ) )
                {
                    monitor.Error( $"Invalid secret name at \"{c.Path}\": a name is made of segments separated by ':' made of letters, digits, '_' and '-' (\"Duende:ClientSecret\")." );
                    success = false;
                }
                if( section.GetChildren().Skip( 1 ).Any() )
                {
                    monitor.Error( $"\"{section.Path}\" is a secret handle: \"{ConfigurationSecret.HandleKey}\" must be its only key." );
                    success = false;
                }
            }
            else
            {
                success &= CheckHandles( monitor, c );
            }
        }
        return success;
    }

    /// <summary>
    /// Reads the secrets of the development (the <see cref="ConfigurationSecret.DevFileName"/> file of the "#Dev" folder of
    /// the solution: the git working folder of the builder) for the engines that need a value at build time (the setup
    /// of a development database). They are never projected nor burnt. A missing file is an empty set.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="secrets">The secrets by name.</param>
    /// <returns>True on success, false when the file is invalid (errors are logged).</returns>
    public bool TryLoadDevSecrets( IActivityMonitor monitor, [NotNullWhen( true )] out IReadOnlyDictionary<string, string>? secrets )
    {
        secrets = null;
        if( !LocalDevSolution.TryFindSolutionFolder( _builderFolder, out var solutionFolder, out _, out _ ) )
        {
            secrets = new Dictionary<string, string>();
            return true;
        }
        var file = solutionFolder.AppendPart( "#Dev" ).AppendPart( ConfigurationSecret.DevFileName );
        if( !ConfigurationSecret.TryReadFile( file, out var s, out var error ) )
        {
            monitor.Error( error );
            return false;
        }
        secrets = s;
        return true;
    }
}

/// <summary>
/// A secret declared by a configurable type of an App (<see cref="ConfigurationSecretAttribute"/>): the builder gives them
/// to <see cref="AppBuilderConfiguration.CheckSecrets"/>.
/// </summary>
/// <param name="TypeName">The name of the type that declares it (for the messages).</param>
/// <param name="Sections">
/// The paths of the sections of the type relative to an anchor: its configurable name for a feature, the section in
/// each of its owners for an option (see <see cref="IAppBuilderContributor.ResolveSections"/>).
/// </param>
/// <param name="Attribute">The attribute.</param>
public sealed record PackageConfigurationSecret( string TypeName, IReadOnlyList<string> Sections, ConfigurationSecretAttribute Attribute );
