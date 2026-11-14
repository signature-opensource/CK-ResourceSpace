using CK.Core;
using System.Collections.Generic;

namespace CK.Setup;

/// <summary>
/// What the <see cref="IAppBuilderContributor"/> receive: the "ckonf.json" of the builder (its <see cref="Apps"/> and its
/// Builder <see cref="Aspects"/>) and the engine configuration they complete.
/// </summary>
public sealed class AppBuilderContext
{
    /// <summary>
    /// Initializes a new context. The builder creates it; this is public for the tests of the contributors.
    /// </summary>
    /// <param name="engineConfiguration">The engine configuration.</param>
    /// <param name="apps">The Apps of the builder, sorted by name.</param>
    /// <param name="aspects">The Builder aspects (the "Aspects" at the root of "ckonf.json"). Defaults to none.</param>
    public AppBuilderContext( EngineConfiguration engineConfiguration, IReadOnlyList<AppConfiguration> apps, AppAspects? aspects = null )
    {
        Throw.CheckNotNullArgument( engineConfiguration );
        Throw.CheckNotNullArgument( apps );
        EngineConfiguration = engineConfiguration;
        Apps = apps;
        Aspects = aspects ?? new AppAspects( null );
    }

    /// <summary>
    /// Gets the engine configuration: its BinPaths are the <see cref="Apps"/> (and the root of an optional "CKSetup.xml").
    /// </summary>
    public EngineConfiguration EngineConfiguration { get; }

    /// <summary>
    /// Gets the Apps of the builder, sorted by name.
    /// </summary>
    public IReadOnlyList<AppConfiguration> Apps { get; }

    /// <summary>
    /// Gets the Builder aspects: the ones that apply to the whole engine run (the "Aspects" at the root of "ckonf.json").
    /// </summary>
    public AppAspects Aspects { get; }
}
