using CK.Core;

namespace CK.Setup;

/// <summary>
/// An App aspect (see <see cref="AppAspectAttribute"/>): a package of a model that the builder finds in the type set of
/// an App, creates from the App's configuration and applies to the App, before the <see cref="IAppBuilderContributor"/>
/// run.
/// <para>
/// A concrete aspect has one public constructor whose optional parameters are its section (a plain
/// <c>Microsoft.Extensions.Configuration.IConfiguration</c>, null when the aspect is present without section: its
/// activation, or <c>true</c>) and its options (an <c>IReadOnlyList&lt;IAppAspectOption&lt;TAspect&gt;&gt;</c>, see
/// <see cref="IAppAspectOption{TAspect}"/>: the enabled options in their order). The constructor reads its data and throws on an
/// invalid value. One instance is created
/// per App.
/// </para>
/// </summary>
public interface IAppAspect : IResourceGroup
{
    /// <summary>
    /// Applies this aspect to an App: typically ensures and configures its aspect configurations in the
    /// <see cref="AppConfiguration.BinPathConfiguration"/> (and the <see cref="AppBuilderContext.EngineConfiguration"/>),
    /// and projects its run time settings in the host (<see cref="AppConfiguration.ProjectValue(string, string)"/>).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The builder context.</param>
    /// <param name="app">The App.</param>
    /// <returns>True on success, false on error (errors must be logged).</returns>
    bool Apply( IActivityMonitor monitor, AppBuilderContext context, AppConfiguration app );
}
