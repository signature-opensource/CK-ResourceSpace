using System;
using System.IO;

namespace CK.Core;

/// <summary>
/// Captures a file or folder change event.
/// </summary>
public sealed class PathChangedEvent
{
    readonly IResPackageResources _resources;
    readonly string _fullPath;
    bool? _fileExists;
    bool? _pathExists;

    /// <summary>
    /// Initializes a new <see cref="PathChangedEvent"/>.
    /// </summary>
    /// <param name="resources">The resources that contains the changed path.</param>
    /// <param name="fullPath">The full changed path.</param>
    public PathChangedEvent( IResPackageResources resources, string fullPath )
    {
        _resources = resources;
        _fullPath = fullPath;
    }

    /// <summary>
    /// Gets the sub path relative to <see cref="Resources"/>.
    /// </summary>
    public ReadOnlySpan<char> SubPath
    {
        get
        {
            Throw.DebugAssert( Resources.LocalPath != null );
            return FullPath.AsSpan( Resources.LocalPath.Length );
        }
    }

    /// <summary>
    /// Gets the resources that contains the changed path.
    /// <see cref="IResPackageResources.LocalPath"/> is necessarily not null.
    /// </summary>
    public IResPackageResources Resources => _resources;

    /// <summary>
    /// Gets whether <paramref name="subPath"/> is equal to <see cref="SubPath"/> or
    /// starts with the <see cref="SubPath"/> and the <see cref="Path.DirectorySeparatorChar"/>.
    /// </summary>
    /// <param name="subPath">The path to challenge.</param>
    /// <returns>True if the <paramref name="subPath"/> is <see cref="SubPath"/> or below it.</returns>
    public bool MatchSubPath( ReadOnlySpan<char> subPath )
    {
        var root = SubPath;
        int delta = subPath.Length - root.Length;
        if( delta < 0 ) return false;
        if( delta > 0 )
        {
            return subPath[root.Length] == Path.DirectorySeparatorChar && subPath.StartsWith( root, StringComparison.Ordinal );
        }
        return subPath.Equals( root, StringComparison.Ordinal );
    }

    /// <summary>
    /// Gets the full changed path.
    /// </summary>
    public string FullPath => _fullPath;

    /// <summary>
    /// Gets whether this <see cref="FullPath"/> is a file that exists.
    /// </summary>
    public bool FileExists => _fileExists ??= File.Exists( FullPath );

    /// <summary>
    /// Gets whether this <see cref="FullPath"/> is a path that exists.
    /// </summary>
    public bool PathExists => _pathExists ??= Path.Exists( FullPath );
}
