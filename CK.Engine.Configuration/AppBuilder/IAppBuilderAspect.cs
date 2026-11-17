using CK.Core;
using System.Collections.Generic;

namespace CK.Setup;

/// <summary>
/// A Builder aspect (see <see cref="AppBuilderAspectAttribute"/>): a package of a model that the builder finds in the
/// type sets of its Apps, creates from its configuration and applies to the engine configuration, before the
/// <see cref="IAppBuilderContributor"/> run.
/// <para>
/// A concrete aspect has one public constructor whose single optional parameter is its section: a plain
/// <c>Microsoft.Extensions.Configuration.IConfiguration</c>, null when the aspect is present without section (its
/// activation, or <c>true</c>). The constructor reads its data and throws on an invalid value.
/// </para>
/// </summary>
public interface IAppBuilderAspect : IResourceGroup
{
    /// <summary>
    /// Applies this aspect: typically ensures and configures its engine aspect configurations in the
    /// <see cref="AppBuilderContext.EngineConfiguration"/>, and projects its run time settings in the hosts of
    /// <paramref name="apps"/> (<see cref="AppConfiguration.ProjectValue(string, string)"/>).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The builder context.</param>
    /// <param name="apps">The Apps whose type set contains this aspect.</param>
    /// <returns>True on success, false on error (errors must be logged).</returns>
    bool Apply( IActivityMonitor monitor, AppBuilderContext context, IReadOnlyList<AppConfiguration> apps );
}
