using CK.Core;
using System;
using System.Collections.Generic;

namespace CK.Setup;

/// <summary>
/// An App of the builder: declared in "ckonf.json" by its name ("Apps:{Name}"), it is its ".App" project (its code: the
/// <see cref="BinPathConfiguration.Path"/> of its engine BinPath) and its ".Host" project (its process: the
/// <see cref="BinPathConfiguration.ProjectPath"/>), that receives the projection of its <see cref="Configuration"/> (its
/// "appsettings.json"). An App hosts its root party and its tenants.
/// </summary>
public sealed class AppConfiguration
{
    readonly HashSet<string> _compileKeys;
    readonly HashSet<string> _declarationKeys;
    readonly Dictionary<string, string> _projectedSections;

    /// <summary>
    /// Initializes a new App. The builder creates them from "ckonf.json"; this is public for the tests of the contributors.
    /// </summary>
    /// <param name="name">The App name.</param>
    /// <param name="appProjectPath">The ".App" project folder.</param>
    /// <param name="hostProjectPath">The ".Host" project folder.</param>
    /// <param name="configuration">The configuration of this App.</param>
    /// <param name="binPathConfiguration">The engine configuration of this App.</param>
    public AppConfiguration( string name,
                             NormalizedPath appProjectPath,
                             NormalizedPath hostProjectPath,
                             ImmutableConfigurationSection configuration,
                             BinPathConfiguration binPathConfiguration )
    {
        Name = name;
        AppProjectPath = appProjectPath;
        HostProjectPath = hostProjectPath;
        Configuration = configuration;
        BinPathConfiguration = binPathConfiguration;
        Aspects = new AppAspects( configuration.TryGetSection( "Aspects" ) );
        _compileKeys = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        _declarationKeys = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        _projectedSections = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
    }

    /// <summary>
    /// Gets the name of this App (its key in "Apps").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the ".App" project folder.
    /// </summary>
    public NormalizedPath AppProjectPath { get; }

    /// <summary>
    /// Gets the ".Host" project folder.
    /// </summary>
    public NormalizedPath HostProjectPath { get; }

    /// <summary>
    /// Gets the configuration of this App ("Apps:{Name}"): the configuration of its host, and its compile time
    /// information.
    /// </summary>
    public ImmutableConfigurationSection Configuration { get; }

    /// <summary>
    /// Gets the aspects of this App ("Apps:{Name}:Aspects").
    /// </summary>
    public AppAspects Aspects { get; }

    /// <summary>
    /// Gets the engine configuration of this App (the engine's BinPath).
    /// </summary>
    public BinPathConfiguration BinPathConfiguration { get; }

    /// <summary>
    /// Declares a compile time key: it is not written in the projection of the <see cref="Configuration"/> (the host's
    /// "appsettings.json"), unless the builder keeps them ("ProjectCompileKeys": true).
    /// </summary>
    /// <param name="path">The path of the key, relative to <see cref="Configuration"/> (with ':' separators).</param>
    public void AddCompileKey( string path )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( path );
        _compileKeys.Add( path );
    }

    /// <summary>
    /// Declares a declaration key: a section whose presence declares something (a feature of a client for instance). When
    /// the projection empties it (all its keys are compile time keys), it is written as <c>true</c>: the declaration
    /// stays.
    /// </summary>
    /// <param name="path">The path of the section, relative to <see cref="Configuration"/> (with ':' separators).</param>
    public void AddDeclarationKey( string path )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( path );
        _declarationKeys.Add( path );
    }

    /// <summary>
    /// Gets the declaration keys (see <see cref="AddDeclarationKey(string)"/>).
    /// </summary>
    public IReadOnlySet<string> DeclarationKeys => _declarationKeys;

    /// <summary>
    /// Gets the compile time keys declared by <see cref="AddCompileKey(string)"/>.
    /// </summary>
    public IReadOnlyCollection<string> CompileKeys => _compileKeys;

    /// <summary>
    /// Declares that a key of the <see cref="Configuration"/> is projected in a section of the host's "appsettings.json":
    /// the keys of a party ("FullName", "Local"...) are directly in the App in "ckonf.json", and in the "CK-AppIdentity"
    /// section of the host.
    /// </summary>
    /// <param name="key">The key of the <see cref="Configuration"/>.</param>
    /// <param name="sectionName">The section of the host's configuration.</param>
    public void ProjectInSection( string key, string sectionName )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( key );
        Throw.CheckNotNullOrWhiteSpaceArgument( sectionName );
        if( _projectedSections.TryGetValue( key, out var already ) && !already.Equals( sectionName, StringComparison.OrdinalIgnoreCase ) )
        {
            Throw.InvalidOperationException( $"The key '{key}' of {this} is already projected in the section '{already}'." );
        }
        _projectedSections[key] = sectionName;
    }

    /// <summary>
    /// Gets the keys declared by <see cref="ProjectInSection(string, string)"/> and their section.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProjectedSections => _projectedSections;

    /// <inheritdoc />
    public override string ToString() => $"App '{Name}'";
}
