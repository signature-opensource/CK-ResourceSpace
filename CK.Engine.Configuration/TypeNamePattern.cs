using System;
using System.Diagnostics.CodeAnalysis;

namespace CK.Setup;

/// <summary>
/// An assembly qualified type name with a <c>*</c> wildcard that selects types: <c>"Type, Assembly"</c> where each part
/// is an exact name or a prefix followed by a <c>*</c>.
/// <list type="bullet">
///     <item><c>"CK.Sample.Front.*, Front.App"</c>: the types of the Front.App assembly whose full name starts with "CK.Sample.Front."
///     (the sub-namespaces and the nested types are included).</item>
///     <item><c>"*, CK.IO.Actor"</c>: all the types of the CK.IO.Actor assembly.</item>
///     <item><c>"CK.IO.*, CK.IO.*"</c>: the types whose full name starts with "CK.IO." in the assemblies whose name starts with "CK.IO.".</item>
/// </list>
/// At least one part contains a <c>*</c>, only as its last character. The assembly part cannot be <c>*</c> alone.
/// </summary>
public sealed class TypeNamePattern
{
    readonly string _pattern;
    readonly string _typePrefix;
    readonly string _assemblyPrefix;
    readonly bool _isTypePrefix;
    readonly bool _isAssemblyPrefix;

    TypeNamePattern( string pattern, string typePrefix, bool isTypePrefix, string assemblyPrefix, bool isAssemblyPrefix )
    {
        _pattern = pattern;
        _typePrefix = typePrefix;
        _isTypePrefix = isTypePrefix;
        _assemblyPrefix = assemblyPrefix;
        _isAssemblyPrefix = isAssemblyPrefix;
    }

    /// <summary>
    /// Gets whether a type name contains a <c>*</c>: it must be parsed as a <see cref="TypeNamePattern"/>.
    /// </summary>
    /// <param name="name">The type name.</param>
    /// <returns>True if the name is a pattern.</returns>
    public static bool IsPattern( string name ) => name.Contains( '*' );

    /// <summary>
    /// Tries to parse a pattern.
    /// </summary>
    /// <param name="pattern">The pattern to parse.</param>
    /// <param name="result">The pattern on success.</param>
    /// <param name="error">The error on failure.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryParse( string pattern,
                                 [NotNullWhen( true )] out TypeNamePattern? result,
                                 [NotNullWhen( false )] out string? error )
    {
        result = null;
        var comma = pattern.IndexOf( ',' );
        if( comma < 0 || pattern.IndexOf( ',', comma + 1 ) >= 0 )
        {
            error = $"Invalid type name pattern '{pattern}': it must be \"Type, Assembly\" (a single comma).";
            return false;
        }
        var typePart = pattern.AsSpan( 0, comma ).Trim();
        var assemblyPart = pattern.AsSpan( comma + 1 ).Trim();
        if( !CheckPart( pattern, "type", typePart, out var typePrefix, out var isTypePrefix, out error )
            || !CheckPart( pattern, "assembly", assemblyPart, out var assemblyPrefix, out var isAssemblyPrefix, out error ) )
        {
            return false;
        }
        if( !isTypePrefix && !isAssemblyPrefix )
        {
            error = $"Invalid type name pattern '{pattern}': it contains no '*'.";
            return false;
        }
        if( isAssemblyPrefix && assemblyPrefix.Length == 0 )
        {
            error = $"Invalid type name pattern '{pattern}': the assembly cannot be '*' alone.";
            return false;
        }
        result = new TypeNamePattern( $"{typePart}, {assemblyPart}", typePrefix, isTypePrefix, assemblyPrefix, isAssemblyPrefix );
        return true;

        static bool CheckPart( string pattern,
                               string partName,
                               ReadOnlySpan<char> part,
                               out string prefix,
                               out bool isPrefix,
                               [NotNullWhen( false )] out string? error )
        {
            prefix = string.Empty;
            isPrefix = false;
            if( part.Length == 0 )
            {
                error = $"Invalid type name pattern '{pattern}': the {partName} is missing.";
                return false;
            }
            int star = part.IndexOf( '*' );
            if( star >= 0 && star != part.Length - 1 )
            {
                error = $"Invalid type name pattern '{pattern}': a '*' can only end the {partName}.";
                return false;
            }
            isPrefix = star >= 0;
            prefix = isPrefix ? part.Slice( 0, star ).ToString() : part.ToString();
            error = null;
            return true;
        }
    }

    /// <summary>
    /// Parses a pattern or throws a <see cref="FormatException"/>.
    /// </summary>
    /// <param name="pattern">The pattern to parse.</param>
    /// <returns>The pattern.</returns>
    public static TypeNamePattern Parse( string pattern )
    {
        return TryParse( pattern, out var result, out var error ) ? result : throw new FormatException( error );
    }

    /// <summary>
    /// Gets whether a type matches this pattern.
    /// </summary>
    /// <param name="type">The type to test.</param>
    /// <returns>True if the type matches.</returns>
    public bool Matches( Type type )
    {
        var fullName = type.FullName;
        var assemblyName = type.Assembly.GetName().Name;
        return fullName != null
               && assemblyName != null
               && Match( fullName, _typePrefix, _isTypePrefix )
               && Match( assemblyName, _assemblyPrefix, _isAssemblyPrefix );

        static bool Match( string name, string prefix, bool isPrefix )
        {
            return isPrefix
                    ? name.StartsWith( prefix, StringComparison.Ordinal )
                    : name.Equals( prefix, StringComparison.Ordinal );
        }
    }

    /// <summary>
    /// Returns the normalized pattern ("Type, Assembly").
    /// </summary>
    /// <returns>The pattern.</returns>
    public override string ToString() => _pattern;
}
