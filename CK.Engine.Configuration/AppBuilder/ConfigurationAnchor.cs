using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Setup;

/// <summary>
/// A section of an App declared by a contributor (see <see cref="AppConfiguration.DeclareAnchor"/>): a party for the
/// AppIdentity contributor, of one or more kinds ("Party", "Remote", "OpenedRemoteTemplate"...), whose values may come
/// from fallback sections (the "Defaults" above it).
/// </summary>
public sealed class ConfigurationAnchor
{
    readonly HashSet<string> _kinds;

    internal ConfigurationAnchor( string path, IEnumerable<string> kinds, IEnumerable<string>? fallbacks )
    {
        Path = path;
        _kinds = new HashSet<string>( kinds, StringComparer.OrdinalIgnoreCase );
        Fallbacks = fallbacks?.ToArray() ?? [];
    }

    /// <summary>
    /// Gets the path of the anchor, relative to the <see cref="AppConfiguration.Configuration"/>.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the kinds of the anchor (case insensitive).
    /// </summary>
    public IReadOnlySet<string> Kinds => _kinds;

    /// <summary>
    /// Gets the fallbacks of the values of the anchor (relative to the <see cref="AppConfiguration.Configuration"/>), the
    /// nearest first: a value of the anchor is its own one, otherwise the one of the first fallback that has it.
    /// </summary>
    public IReadOnlyList<string> Fallbacks { get; }

    internal void AddKinds( IEnumerable<string> kinds ) => _kinds.UnionWith( kinds );

    /// <inheritdoc />
    public override string ToString() => $"'{Path}' ({_kinds.Concatenate( ", " )})";
}
