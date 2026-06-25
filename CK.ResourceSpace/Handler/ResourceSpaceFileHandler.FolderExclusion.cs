using CK.EmbeddedResources;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace CK.Core;

public abstract partial class ResourceSpaceFileHandler
{
    /// <summary>
    /// Provides a filter for <see cref="ResourceLocator"/> and <see cref="ResourceLocator"/>
    /// that are handled by any of the registered <see cref="ResourceSpaceFolderHandler"/>.
    /// </summary>
    public readonly struct FolderExclusion
    {
        readonly ImmutableArray<string> _folders;

        internal FolderExclusion( ImmutableArray<ResourceSpaceFolderHandler> folders )
        {
            _folders = folders.Select( f => f.RootFolderName ).ToImmutableArray();
        }

        /// <summary>
        /// Gets all the <see cref="ResourceSpaceFolderHandler.RootFolderName"/>.
        /// </summary>
        public ImmutableArray<string> ExcludedFolderNames => _folders;

        /// <summary>
        /// Checks whether the <paramref name="resource"/> is handled by an
        /// existing <see cref="ResourceSpaceFolderHandler"/>.
        /// </summary>
        /// <param name="resource">The resource.</param>
        /// <returns>True if the resource is already handled and should be ignored.</returns>
        public bool IsExcluded( ResourceLocator resource ) => IsExcluded( resource.ResourceName );

        /// <summary>
        /// Checks whether the <paramref name="folder"/> is handled by an
        /// existing <see cref="ResourceSpaceFolderHandler"/>.
        /// </summary>
        /// <param name="folder">The folder.</param>
        /// <returns>True if the folder is already handled and should be ignored.</returns>
        public bool IsExcluded( ResourceFolder folder ) => IsExcluded( folder.FolderName );

        bool IsExcluded( ReadOnlySpan<char> path )
        {
            foreach( var p in _folders )
            {
                int len = p.Length;
                if( path.Length > len
                    && (path[len] == '\\' || path[len] == '/')
                    && path.StartsWith( p, StringComparison.Ordinal ) )
                {
                    return true;
                }
            }
            return false;
        }
    }
}
