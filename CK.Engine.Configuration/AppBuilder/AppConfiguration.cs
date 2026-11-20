using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

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
    readonly Dictionary<string, string> _projectedValues;
    readonly Dictionary<string, string> _projectedValueOrigins;
    readonly List<ConfigurationLockEntry> _locks;
    readonly Dictionary<string, ConfigurationAnchor> _anchors;
    readonly HashSet<string> _initSections;
    readonly HashSet<string> _burntKeys;
    readonly HashSet<string> _runValues;
    readonly HashSet<string> _secretKeys;
    readonly HashSet<string> _perDeploymentKeys;
    readonly HashSet<(string Kind, string Path)> _perDeploymentPartyKeys;

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
        _secretKeys = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        _perDeploymentKeys = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        _perDeploymentPartyKeys = new HashSet<(string, string)>( KindPathComparer.Instance );
        _declarationKeys = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        _projectedSections = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
        _projectedValues = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
        _projectedValueOrigins = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
        _locks = new List<ConfigurationLockEntry>();
        _anchors = new Dictionary<string, ConfigurationAnchor>( StringComparer.OrdinalIgnoreCase );
        _initSections = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        _burntKeys = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        _runValues = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
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
    /// Declares that the values at a path are secrets (see <see cref="ConfigurationSecret"/>): their configuration should
    /// be a handle. For the readers that read their keys by hand; a configurable type declares its secrets with
    /// <see cref="ConfigurationSecretAttribute"/>. See <see cref="AppBuilderConfiguration.CheckSecrets"/>.
    /// </summary>
    /// <param name="path">
    /// The path of the values, relative to <see cref="Configuration"/> (with ':' separators): it may contain the patterns
    /// of the configuration paths (<c>*</c> for any member, <c>[*]</c> for any item).
    /// </param>
    public void AddSecretKey( string path )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( path );
        _secretKeys.Add( path );
    }

    /// <summary>
    /// Gets the secret keys declared by <see cref="AddSecretKey(string)"/>.
    /// </summary>
    public IReadOnlyCollection<string> SecretKeys => _secretKeys;

    /// <summary>
    /// Declares that the values at a path are "per deployment" (see <see cref="CentralDeployment"/>): each deployment must
    /// write them itself. For the readers that read their keys by hand; a configurable type declares them with
    /// <c>[ConfigurationPerDeployment]</c>.
    /// </summary>
    /// <param name="path">
    /// The path of the values, relative to <see cref="Configuration"/> (with ':' separators): it may contain the patterns
    /// of the configuration paths (<c>*</c> for any member, <c>[*]</c> for any item).
    /// </param>
    public void AddPerDeploymentKey( string path )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( path );
        _perDeploymentKeys.Add( path );
    }

    /// <summary>
    /// Gets the keys declared by <see cref="AddPerDeploymentKey(string)"/>.
    /// </summary>
    public IReadOnlyCollection<string> PerDeploymentKeys => _perDeploymentKeys;

    /// <summary>
    /// Declares that a property of the parties of a kind is "per deployment" (see <see cref="CentralDeployment"/>): the
    /// "Address" of a remote is <c>AddPerDeploymentPartyKey( "Remote", "Address" )</c>. The property is directly in each
    /// anchor of the kind (and in its fallbacks), not in the section of a feature (this is what
    /// <c>[ConfigurationPerDeployment]</c> declares).
    /// </summary>
    /// <param name="kind">The kind of the anchors (see <see cref="DeclareAnchor"/>).</param>
    /// <param name="path">The path of the property in the party (with ':' separators, patterns allowed).</param>
    public void AddPerDeploymentPartyKey( string kind, string path )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( kind );
        Throw.CheckNotNullOrWhiteSpaceArgument( path );
        _perDeploymentPartyKeys.Add( (kind, path) );
    }

    /// <summary>
    /// Gets the keys declared by <see cref="AddPerDeploymentPartyKey(string, string)"/>.
    /// </summary>
    public IReadOnlyCollection<(string Kind, string Path)> PerDeploymentPartyKeys => _perDeploymentPartyKeys;

    sealed class KindPathComparer : IEqualityComparer<(string Kind, string Path)>
    {
        public static readonly KindPathComparer Instance = new();

        public bool Equals( (string Kind, string Path) x, (string Kind, string Path) y )
            => StringComparer.OrdinalIgnoreCase.Equals( x.Kind, y.Kind ) && StringComparer.OrdinalIgnoreCase.Equals( x.Path, y.Path );

        public int GetHashCode( (string Kind, string Path) o )
            => HashCode.Combine( StringComparer.OrdinalIgnoreCase.GetHashCode( o.Kind ), StringComparer.OrdinalIgnoreCase.GetHashCode( o.Path ) );
    }

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

    /// <summary>
    /// Projects a value at a path of the host's "appsettings.json" (outside its App's keys): the run time settings of an
    /// aspect ("SqlServer:DefaultDatabaseConnectionString"). The first part of the path is a root section of the host
    /// that can't be a key of the App.
    /// </summary>
    /// <param name="path">The path in the host's configuration (':' separated).</param>
    /// <param name="value">The value.</param>
    public void ProjectValue( string path, string value )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( path );
        Throw.CheckNotNullArgument( value );
        if( _projectedValues.TryGetValue( path, out var already ) && already != value )
        {
            Throw.InvalidOperationException( $"The path '{path}' of {this} is already projected with another value." );
        }
        _projectedValues[path] = value;
    }

    /// <summary>
    /// Projects a value at a path of the host's "appsettings.json" (see <see cref="ProjectValue(string, string)"/>) from
    /// a value of the "ckonf.json": when a <c>"$LOCK"</c> restricts this origin to <see cref="ConfigurationLevel.Compile"/>,
    /// the value is burnt in the generated code instead of being written in the host's file.
    /// </summary>
    /// <param name="path">The path in the host's configuration (':' separated).</param>
    /// <param name="value">The value.</param>
    /// <param name="origin">The configuration path of the value it comes from ("ckonf:Aspects:SqlServer:DefaultDatabaseConnectionString").</param>
    public void ProjectValue( string path, string value, string origin )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( origin );
        ProjectValue( path, value );
        _projectedValueOrigins[path] = origin;
    }

    /// <summary>
    /// Gets the values projected by <see cref="ProjectValue(string, string)"/>, by path.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProjectedValues => _projectedValues;

    /// <summary>
    /// Gets the origins of the projected values (see <see cref="ProjectValue(string, string, string)"/>), by path.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProjectedValueOrigins => _projectedValueOrigins;

    /// <summary>
    /// Gets the entries of the <c>"$LOCK"</c> of this App (removed from its <see cref="Configuration"/>).
    /// </summary>
    public IReadOnlyList<ConfigurationLockEntry> Locks => _locks;

    /// <summary>
    /// Adds an entry of a <c>"$LOCK"</c> of this App. The builder adds the ones of the "ckonf.json"; this is public for
    /// the tests.
    /// </summary>
    /// <param name="entry">The entry.</param>
    public void AddLock( ConfigurationLockEntry entry )
    {
        Throw.CheckNotNullArgument( entry );
        _locks.Add( entry );
    }

    /// <summary>
    /// Declares an anchor: a section of a kind (a party: "Party", "Remote", "OpenedRemoteTemplate"...) whose values may
    /// come from fallback sections (the "Defaults" above a party). A <c>"$LOCK"</c> in an anchor, or in one of its
    /// fallbacks, locks the value of the anchor: its own value, otherwise the one of its first fallback that has it. The
    /// restrictions of the packages (<see cref="ConfigurationLockAttribute"/>) target the anchors by their kinds.
    /// Declaring a path twice adds its kinds.
    /// </summary>
    /// <param name="path">The path of the anchor, relative to <see cref="Configuration"/>.</param>
    /// <param name="kinds">The kinds of the anchor.</param>
    /// <param name="fallbacks">The fallbacks of the values of the anchor (relative to <see cref="Configuration"/>), the nearest first.</param>
    public void DeclareAnchor( string path, IEnumerable<string> kinds, IEnumerable<string>? fallbacks = null )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( path );
        Throw.CheckNotNullArgument( kinds );
        if( _anchors.TryGetValue( path, out var a ) )
        {
            a.AddKinds( kinds );
        }
        else
        {
            _anchors.Add( path, new ConfigurationAnchor( path, kinds, fallbacks ) );
        }
    }

    /// <summary>
    /// Gets the anchors declared by <see cref="DeclareAnchor(string, IEnumerable{string}, IEnumerable{string}?)"/>.
    /// </summary>
    public IReadOnlyCollection<ConfigurationAnchor> Anchors => _anchors.Values;

    /// <summary>
    /// Declares a section that the code reads when the application starts (<see cref="ConfigurationLevel.Init"/>): a
    /// <c>"$LOCK"</c> can restrict its values to <see cref="ConfigurationLevel.Compile"/>. A <c>"$LOCK"</c> on a value
    /// that is in no declared section (and that is not a compile key) is an error.
    /// </summary>
    /// <param name="path">The path of the section, relative to <see cref="Configuration"/>.</param>
    public void DeclareInitSection( string path )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( path );
        _initSections.Add( path );
    }

    /// <summary>
    /// Gets the sections declared by <see cref="DeclareInitSection(string)"/>.
    /// </summary>
    public IReadOnlyCollection<string> InitSections => _initSections;

    /// <summary>
    /// Declares a value that its code can change while the application runs (<see cref="ConfigurationLevel.Run"/>): a
    /// <c>"$LOCK"</c> can restrict it to <see cref="ConfigurationLevel.Init"/> or <see cref="ConfigurationLevel.Compile"/>.
    /// Its section must be declared (<see cref="DeclareInitSection(string)"/>).
    /// </summary>
    /// <param name="path">The path of the value, relative to <see cref="Configuration"/>.</param>
    public void DeclareRunValue( string path )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( path );
        _runValues.Add( path );
    }

    /// <summary>
    /// Gets the values declared by <see cref="DeclareRunValue(string)"/>.
    /// </summary>
    public IReadOnlyCollection<string> RunValues => _runValues;

    /// <summary>
    /// Gets the keys (relative to <see cref="Configuration"/>) whose values are burnt in the generated code: they are
    /// not written in the host's "appsettings.json".
    /// </summary>
    public IReadOnlyCollection<string> BurntKeys => _burntKeys;

    internal void AddBurntKey( string path ) => _burntKeys.Add( path );

    /// <summary>
    /// Gets the path of a key of the <see cref="Configuration"/> in the host's configuration: in its section when its
    /// first segment is projected in one (see <see cref="ProjectInSection(string, string)"/>).
    /// </summary>
    /// <param name="path">The path relative to <see cref="Configuration"/>.</param>
    /// <returns>The path in the host's configuration.</returns>
    public string ToHostPath( string path )
    {
        var first = path.Split( ':', 2 )[0];
        return _projectedSections.TryGetValue( first, out var section ) ? $"{section}:{path}" : path;
    }

    /// <inheritdoc />
    public override string ToString() => $"App '{Name}'";
}
