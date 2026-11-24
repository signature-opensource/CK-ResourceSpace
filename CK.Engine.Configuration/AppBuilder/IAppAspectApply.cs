using CK.Core;

namespace CK.Setup;

/// <summary>
/// What an App aspect does: an <see cref="IAppAspect"/> (declared by CK.AppBuilder.Abstractions) implements it. The App
/// builder reports an error for an App aspect that doesn't.
/// </summary>
public interface IAppAspectApply : IAppAspect
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
