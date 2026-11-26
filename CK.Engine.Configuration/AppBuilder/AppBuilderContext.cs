using CK.Core;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CK.Setup;

/// <summary>
/// What the <see cref="IAppBuilderContributor"/> receive: the AppContext that the Builder builds (its <see cref="Apps"/> and its
/// AppContext <see cref="Aspects"/>) and the engine configuration they complete.
/// </summary>
public sealed class AppBuilderContext
{
    /// <summary>
    /// Initializes a new context. The builder creates it; this is public for the tests of the contributors.
    /// </summary>
    /// <param name="engineConfiguration">The engine configuration.</param>
    /// <param name="apps">The Apps of the AppContext, sorted by name.</param>
    /// <param name="aspects">The AppContext aspects (the "Aspects" at the root of "ckonf.json"). Defaults to none.</param>
    /// <param name="devSecrets">
    /// The secrets of the development (see <see cref="AppContextConfiguration.TryLoadDevSecrets"/>). Defaults to none.
    /// </param>
    /// <param name="deployment">
    /// The deployment that this run builds (see <see cref="CentralDeployment"/>), null for the development build.
    /// </param>
    public AppBuilderContext( EngineConfiguration engineConfiguration,
                              IReadOnlyList<AppConfiguration> apps,
                              AppAspects? aspects = null,
                              IReadOnlyDictionary<string, string>? devSecrets = null,
                              CentralDeployment? deployment = null )
    {
        Deployment = deployment;
        Throw.CheckNotNullArgument( engineConfiguration );
        Throw.CheckNotNullArgument( apps );
        EngineConfiguration = engineConfiguration;
        Apps = apps;
        Aspects = aspects ?? new AppAspects( null );
        _devSecrets = devSecrets ?? new Dictionary<string, string>();
    }

    readonly IReadOnlyDictionary<string, string> _devSecrets;

    /// <summary>
    /// Resolves a secret (see <see cref="ConfigurationSecret"/>) for an engine that needs its value at build time (the
    /// setup of a development database): from the secrets of the development (the "#Dev/Secrets.json" file of the
    /// solution). The value must never be projected nor burnt: the handle is.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="name">The name of the secret.</param>
    /// <param name="value">The value on success.</param>
    /// <returns>True on success, false when the secret is missing (the error is logged).</returns>
    public bool TryResolveSecret( IActivityMonitor monitor, string name, [NotNullWhen( true )] out string? value )
    {
        if( Deployment != null )
        {
            value = null;
            monitor.Error( $"The secret '{name}' is required by the build of the deployment '{Deployment.Name}': the build of a deployment never reads a secret (its value is given to the deployed host)." );
            return false;
        }
        if( name.Contains( '{' ) )
        {
            value = null;
            monitor.Error( $"The secret '{name}' is required by the build: its name uses the placeholders of the instances of a fleet, and the build is the one of all its instances." );
            return false;
        }
        if( _devSecrets.TryGetValue( name, out value ) ) return true;
        monitor.Error( $"The secret '{name}' is required by the build: its value is missing from \"#Dev/{ConfigurationSecret.DevFileName}\" (its root is the content of the \"{ConfigurationSecret.SectionName}\" section)." );
        return false;
    }

    /// <summary>
    /// Gets the deployment that this run builds (a builder generated for it: see <see cref="CentralDeployment"/>), or
    /// null for the development build. The build of a deployment never touches a database, never reads a secret
    /// (<see cref="TryResolveSecret"/> fails).
    /// </summary>
    public CentralDeployment? Deployment { get; }

    /// <summary>
    /// Gets the engine configuration: its BinPaths are the <see cref="Apps"/> (and the root of an optional "CKSetup.xml").
    /// </summary>
    public EngineConfiguration EngineConfiguration { get; }

    /// <summary>
    /// Gets the Apps of the AppContext, sorted by name.
    /// </summary>
    public IReadOnlyList<AppConfiguration> Apps { get; }

    /// <summary>
    /// Gets the AppContext aspects: the ones that apply to the whole engine run (the "Aspects" at the root of "ckonf.json").
    /// </summary>
    public AppAspects Aspects { get; }
}
