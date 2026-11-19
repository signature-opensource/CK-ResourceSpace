using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace CK.Setup;

/// <summary>
/// A pattern of configuration paths (the patterns of a <c>"$LOCK"</c>): the configuration's ':' separated segments,
/// where a whole segment <c>*</c> matches any segment that is not an integer (an object's member) and a whole segment
/// <c>[*]</c> matches an integer segment (an array's item). The matching is case insensitive.
/// <para>
/// A pattern covers a path when it matches its first segments: <c>Endpoints:Http</c> covers
/// <c>Endpoints:Http:Url</c> (a pattern names a value or a section as a whole). An index (<c>Parties:0</c>) is refused:
/// an index is not an identity (an item inserted before shifts it).
/// </para>
/// <para>
/// The runtime keeps its own copy of this matching (CK.AppIdentity's <c>CompiledConfiguration</c>): the engine model is not
/// referenced at run time.
/// </para>
/// </summary>
public sealed class ConfigurationPathPattern
{
    readonly string[] _segments;

    ConfigurationPathPattern( string text, string[] segments )
    {
        Text = text;
        _segments = segments;
    }

    /// <summary>
    /// Gets the text of this pattern.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets whether this pattern has no wildcard: it names one path.
    /// </summary>
    public bool IsLiteral => Array.TrueForAll( _segments, s => s is not ("*" or "[*]") );

    /// <summary>
    /// Tries to parse a pattern.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="pattern">The pattern on success.</param>
    /// <param name="error">The error on failure.</param>
    /// <returns>True on success.</returns>
    public static bool TryParse( string? text, [NotNullWhen( true )] out ConfigurationPathPattern? pattern, [NotNullWhen( false )] out string? error )
    {
        pattern = null;
        if( string.IsNullOrWhiteSpace( text ) )
        {
            error = "an empty pattern";
            return false;
        }
        var segments = text.Split( ':' );
        foreach( var s in segments )
        {
            if( s.Length == 0 || s.Trim().Length != s.Length )
            {
                error = $"the pattern '{text}' has an empty segment (or one with spaces)";
                return false;
            }
            if( IsIndex( s ) )
            {
                error = $"the pattern '{text}' has an index ('{s}'): an index is not an identity, use '[*]' for every item";
                return false;
            }
            if( s != "[*]" && (s.Contains( '[' ) || s.Contains( ']' ) || (s.Contains( '*' ) && s != "*")) )
            {
                error = $"the pattern '{text}' has an invalid segment '{s}': '*' (a member) and '[*]' (an item) are whole segments";
                return false;
            }
        }
        error = null;
        pattern = new ConfigurationPathPattern( text, segments );
        return true;
    }

    /// <summary>
    /// Gets whether this pattern covers a path: it matches its first segments.
    /// </summary>
    /// <param name="path">The path (':' separated).</param>
    /// <returns>True if this pattern covers the path.</returns>
    public bool Covers( string path ) => Covers( path.Split( ':' ) );

    /// <summary>
    /// Gets whether this pattern covers a path: it matches its first segments.
    /// </summary>
    /// <param name="segments">The segments of the path.</param>
    /// <returns>True if this pattern covers the path.</returns>
    public bool Covers( IReadOnlyList<string> segments )
    {
        if( segments.Count < _segments.Length ) return false;
        for( int i = 0; i < _segments.Length; ++i )
        {
            if( !Matches( _segments[i], segments[i] ) ) return false;
        }
        return true;
    }

    static bool Matches( string pattern, string segment ) => pattern switch
    {
        "*" => !IsIndex( segment ),
        "[*]" => IsIndex( segment ),
        _ => pattern.Equals( segment, StringComparison.OrdinalIgnoreCase )
    };

    /// <summary>
    /// Gets whether a segment is an integer (the key of an array's item).
    /// </summary>
    /// <param name="segment">The segment.</param>
    /// <returns>True for an index.</returns>
    public static bool IsIndex( string segment )
    {
        if( segment.Length == 0 ) return false;
        foreach( var c in segment ) if( c is < '0' or > '9' ) return false;
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Text;
}
