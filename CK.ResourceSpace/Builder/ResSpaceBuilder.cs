using CK.EmbeddedResources;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CK.Core;

/// <summary>
/// Handles <see cref="ResSpaceData"/> and enables <see cref="ResourceSpaceFolderHandler"/>
/// and <see cref="ResourceSpaceFileHandler"/> to be registered in order to produce a
/// final <see cref="ResSpace"/>.
/// </summary>
public sealed class ResSpaceBuilder
{
    readonly ResSpaceData _spaceData;
    readonly List<ResourceSpaceFolderHandler> _folderHandlers;
    readonly List<ResourceSpaceFileHandler> _fileHandlers;

    /// <summary>
    /// Initializes a new builder.
    /// </summary>
    /// <param name="spaceData">The space data.</param>
    public ResSpaceBuilder( ResSpaceData spaceData )
    {
        _spaceData = spaceData;
        _folderHandlers = new List<ResourceSpaceFolderHandler>();
        _fileHandlers = new List<ResourceSpaceFileHandler>();
    }

    /// <summary>
    /// Gets the <see cref="ResCoreData"/>.
    /// </summary>
    public ResCoreData SpaceData => _spaceData.CoreData;

    /// <summary>
    /// Gets or sets the configured Code generated resource container.
    /// This can only be set if this has not been previously set (ie. this is null).
    /// See <see cref="ResSpaceConfiguration.GeneratedCodeContainer"/>.
    /// <para>
    /// This is the last possibility to set the generated code.
    /// </para>
    /// </summary>
    [DisallowNull]
    public IResourceContainer? GeneratedCodeContainer
    {
        get => _spaceData.GeneratedCodeContainer;
        set => _spaceData.GeneratedCodeContainer = value;
    }

    /// <summary>
    /// Adds a <see cref="ResourceSpaceFileHandler"/>.
    /// <para>
    /// If a handler with a common <see cref="ResourceSpaceFileHandler.FileExtensions"/> already
    /// exists, a warnig is emitted.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="h">The handler to register.</param>
    /// <returns>True on success, false on error.</returns>
    public bool RegisterHandler( IActivityMonitor monitor, ResourceSpaceFileHandler h )
    {
        Throw.CheckNotNullArgument( h );
        if( _fileHandlers.Contains( h ) )
        {
            monitor.Warn( $"Duplicate handler registration for '{h}'. Ignored." );
            return true;
        }
        var conflict = _fileHandlers.Where(
                            existing => existing.FileExtensions.Any(
                                            f => h.FileExtensions.Any( e => e.Equals( f, StringComparison.OrdinalIgnoreCase ) ) ) );
        if( conflict.Any() )
        {
            monitor.Warn( $"Handler '{h}' has some file extension in common with at least '{conflict.First()}'." );
        }
        _fileHandlers.Add( h );
        return true;
    }

    /// <summary>
    /// Adds a <see cref="ResourceSpaceFolderHandler"/>. 
    /// <para>
    /// If a handler with the same <see cref="ResourceSpaceFolderHandler.RootFolderName"/> already
    /// exists, this is an error.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="h">The handler to register.</param>
    /// <returns>True on success, false on error.</returns>
    public bool RegisterHandler( IActivityMonitor monitor, ResourceSpaceFolderHandler h )
    {
        Throw.CheckNotNullArgument( h );
        if( _folderHandlers.Contains( h ) )
        {
            monitor.Warn( $"Duplicate handler registration for '{h}'. Ignored." );
            return true;
        }
        var conflict = _folderHandlers.Where( existing => h.RootFolderName.Equals( existing.RootFolderName, StringComparison.OrdinalIgnoreCase ) );
        if( conflict.Any() )
        {
            monitor.Error( $"Unable to add handler '{h}'. This folder is already handled by {conflict.First()}." );
            return false;
        }
        _folderHandlers.Add( h );
        return true;
    }

    /// <summary>
    /// Tries to build the <see cref="ResSpace"/> from the <see cref="ResSpaceData"/>. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The space on success, null otherwise.</returns>
    public ResSpace? Build( IActivityMonitor monitor )
    {
        var codeGen = _spaceData.GeneratedCodeContainer;
        if( codeGen == null )
        {
            // Late GeneratedCodeContainer assignment: a ResourceContainerWrapper (around an EmptyResourceContainer)
            // has been created.
            Throw.DebugAssert( _spaceData.CoreData.CodePackage.AfterResources.Resources is ResourceContainerWrapper );

            // Definitly assigns the code generated container. This is a no-op
            // for the ResourceContainerWrapper.InnerContainer (assigned to itself)
            // but this "publish" the wrapped empty container in the _generatedCodeContainer
            // core data field.
            // This "closes" the possibilty to re-assign it.
            var r = (ResourceContainerWrapper)_spaceData.CoreData.CodePackage.AfterResources.Resources;
            _spaceData.GeneratedCodeContainer = r;
            // For coherency, if the code container is a CodeGenResourceContainer (which is often the case),
            // we close it just like we close any packages CodeGenResourceContainer.
            if( r.InnerContainer is CodeGenResourceContainer c ) c.Close();
        }
        else
        {
            // If the assigned container is a CodeGenResourceContainer, close it also.
            if( codeGen is CodeGenResourceContainer c )
            {
                c.Close();
            }
        }
        // Closes any CodeGenResourceContainer that may appear in regular packages.
        // Foreach casting: regular packages have StoreContainer resources and we skip the <Code> and <App> before/after. 
        foreach( StoreContainer resources in _spaceData.CoreData.AllPackageResources.Take( 2..^2 ).Select( r => r.Resources ) )
        {
            if( resources.InnerContainer is CodeGenResourceContainer c ) c.Close();
        }
        _spaceData.Close();
        var space = new ResSpace( _spaceData.CoreData, _folderHandlers.ToImmutableArray(), _fileHandlers.ToImmutableArray() );
        return space.Initialize( monitor )
                ? space
                : null;
    }
}
