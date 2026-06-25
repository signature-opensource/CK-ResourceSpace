using CK.EmbeddedResources;
using System;
using System.IO;

namespace CK.Core;

/// <summary>
/// A resource installer has a very basic interface. It must be associated to one or more <see cref="ResourceSpaceFolderHandler"/>
/// or <see cref="ResourceSpaceFileHandler"/>.
/// </summary>
public interface IResourceSpaceItemInstaller
{
    /// <summary>
    /// Called before the installation, once all the handlers have been successfully initialized.
    /// </summary>
    /// <param name="monitor">The monitor to uses.</param>
    /// <param name="resSpace">The resource space.</param>
    /// <returns>True on success, false if anything prevents the install to succeed (errors should be logged).</returns>
    bool Open( IActivityMonitor monitor, ResSpace resSpace );

    /// <summary>
    /// Pushes a temporary sub path that will prefix any target path.
    /// </summary>
    /// <param name="subPath">Sub path must not be empty nor <see cref="NormalizedPath.IsRooted"/>.</param>
    /// <returns>A disposable that restores the initial path.</returns>
    IDisposable PushSubPath( NormalizedPath subPath );

    /// <summary>
    /// Writes a resource content.
    /// Its target file path is <see cref="ResourceLocator.ResourceName"/>.
    /// </summary>
    /// <param name="resource">The resource to write.</param>
    void Write( ResourceLocator resource );

    /// <summary>
    /// Opens a writable stream at a given path.
    /// </summary>
    /// <param name="path">The target item path.</param>
    /// <returns>The stream that must be disposed once written.</returns>
    Stream OpenWriteStream( NormalizedPath path );

    /// <summary>
    /// Writes a resource content.
    /// </summary>
    /// <param name="path">The target item path.</param>
    /// <param name="resource">The resource to write.</param>
    void Write( NormalizedPath path, ResourceLocator resource );

    /// <summary>
    /// Writes a text content.
    /// </summary>
    /// <param name="path">The target item path.</param>
    /// <param name="text">The text content to write.</param>
    void Write( NormalizedPath path, string text );

    /// <summary>
    /// Must release any acquired resources.
    /// This can optionally handle an install failure by rolling back changes.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="success">Whether the install succeeded or not.</param>
    void Close( IActivityMonitor monitor, bool success );
}
