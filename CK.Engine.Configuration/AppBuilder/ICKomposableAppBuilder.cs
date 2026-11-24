using CK.Core;

namespace CK.Setup;

/// <summary>
/// Helper for ".Builder" projects that handles the setup from a ".App" to a ".Host" projects.
/// This is implemented by CKomposableAppBuilder in the CK.AppBuilder package; the engines extend it (the TypeScript
/// engine ensures its aspect configuration with it, for instance).
/// </summary>
public interface ICKomposableAppBuilder
{
    /// <summary>
    /// Gets or sets the application name.
    /// When conventions are followed, this is the prefix of this "XXX.Builder" project name.
    /// </summary>
    string ApplicationName { get; set; }

    /// <summary>
    /// Gets this builder project path.
    /// </summary>
    NormalizedPath BuilderFolderPath { get; }

    /// <summary>
    /// Gets the engine configuration.
    /// </summary>
    EngineConfiguration EngineConfiguration { get; }

    /// <summary>
    /// Gets the monitor.
    /// </summary>
    IActivityMonitor Monitor { get; }

    /// <summary>
    /// Gets the path from this project to its bin path: "bin/Debug/net8.0".
    /// This is used by <see cref="GetAppBinPath(string?)"/>.
    /// </summary>
    NormalizedPath MsBuildOutputPath { get; }

    /// <summary>
    /// Gets the parent folder of the this "XXX.Builder" and the "XXX.App" and "XXX.Host" projects.
    /// </summary>
    NormalizedPath ParentFolderPath { get; }

    /// <summary>
    /// Gets the "XXX.App" folder path. The <paramref name="appName"/> defaults to <see cref="ApplicationName"/>.
    /// </summary>
    /// <param name="appName">The application name to build.</param>
    /// <returns>The "XXX.App" folder path.</returns>
    NormalizedPath GetAppFolderPath( string? appName = null );

    /// <summary>
    /// Gets the "XXX.Host" folder path. The <paramref name="appName"/> defaults to <see cref="ApplicationName"/>.
    /// </summary>
    /// <param name="appName">The application name to build.</param>
    /// <returns>The "XXX.Host" folder path.</returns>
    NormalizedPath GetHostFolderPath( string? appName = null );

    /// <summary>
    /// Gets the <see cref="BinPathConfiguration.Path"/> to use.
    /// It is "<see cref="GetAppBinPath(string?)"/>/<see cref="MsBuildOutputPath"/>".
    /// </summary>
    /// <param name="appName">The application name to build. Defaults to <see cref="ApplicationName"/>.</param>
    /// <returns>The BinPath to use.</returns>
    NormalizedPath GetAppBinPath( string? appName = null );
}
