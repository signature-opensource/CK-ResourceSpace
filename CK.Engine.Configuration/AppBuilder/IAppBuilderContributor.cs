using CK.Core;

namespace CK.Setup;

/// <summary>
/// A contributor of the CKomposableAppBuilder (CK.AppBuilder), declared by <see cref="AppBuilderContributorAttribute"/>:
/// it reads the "ckonf.json" of the builder (its <see cref="AppBuilderContext.Apps"/> and the Builder
/// <see cref="AppBuilderContext.Aspects"/>) and completes the <see cref="AppBuilderContext.EngineConfiguration"/> (the
/// aspects it deduces from the parties, the ones it reads).
/// <para>
/// A contributor has one public constructor whose parameters are other contributors: a constructor parameter is a
/// requirement (the required contributor is created and runs before). A cycle is an error.
/// </para>
/// </summary>
public interface IAppBuilderContributor
{
    /// <summary>
    /// Called when the solution has a <see cref="CentralConfiguration"/>, before the "ckonf.json" of the builder is
    /// written from it (and read): the contributor claims the root keys of the central configuration it handles
    /// (<see cref="CentralProjectionContext.ClaimKey(string)"/>) and completes the JSON of the Apps of the builder
    /// (<see cref="CentralProjectionContext.Apps"/>). Called once, in the order of the requirements, before
    /// <see cref="Contribute(IActivityMonitor, AppBuilderContext)"/>. Does nothing by default.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The projection context.</param>
    /// <returns>True on success, false on error (errors must be logged).</returns>
    bool ProjectFromCentral( IActivityMonitor monitor, CentralProjectionContext context ) => true;

    /// <summary>
    /// Contributes to the engine configuration. Called once, in the order of the requirements.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The context.</param>
    /// <returns>True on success, false on error (errors must be logged).</returns>
    bool Contribute( IActivityMonitor monitor, AppBuilderContext context );

    /// <summary>
    /// Called once all the contributors have contributed, in the same order: a contributor can check what the others did.
    /// Does nothing by default.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The context.</param>
    /// <returns>True on success, false on error (errors must be logged).</returns>
    bool OnContributed( IActivityMonitor monitor, AppBuilderContext context ) => true;
}
