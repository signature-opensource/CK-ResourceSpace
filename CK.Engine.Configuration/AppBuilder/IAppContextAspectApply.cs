using CK.Core;
using System.Collections.Generic;

namespace CK.Setup;

/// <summary>
/// What an AppContext aspect does: an <see cref="IAppContextAspect"/> (declared by CK.AppBuilder.Abstractions) implements it.
/// The App builder reports an error for an AppContext aspect that doesn't.
/// </summary>
public interface IAppContextAspectApply : IAppContextAspect
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
