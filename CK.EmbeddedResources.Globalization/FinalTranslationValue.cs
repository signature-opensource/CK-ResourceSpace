using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.EmbeddedResources;

/// <summary>
/// A translation value is a <see cref="Text"/> that comes from an <see cref="Origin"/>
/// with potential <see cref="Ambiguities"/>.
/// <para>
/// Equality ignores the <see cref="Ambiguities"/>: two values are equal if their <see cref="Text"/>
/// and <see cref="Origin"/> are equal.
/// </para>
/// </summary>
public readonly struct FinalTranslationValue : IEquatable<FinalTranslationValue>
{
    readonly string _text;
    readonly ResourceLocator _origin;
    readonly IEnumerable<FinalTranslationValue>? _ambiguities;

    /// <summary>
    /// Gets the translation text.
    /// This is empty even when <see cref="IsValid"/> is false.
    /// </summary>
    public string Text => _text ?? string.Empty;

    /// <summary>
    /// Gets the origin of the translation.
    /// </summary>
    public ResourceLocator Origin => _origin;

    /// <summary>
    /// Gets the translations that share the same key if any.
    /// </summary>
    public IEnumerable<FinalTranslationValue>? Ambiguities => _ambiguities;

    /// <summary>
    /// Gets whether this value is valid.
    /// Invalid value is the <c>default</c> (<see cref="Text"/> is the empty string).
    /// </summary>
    public bool IsValid => _text != null;

    /// <summary>
    /// Initializes a new translation.
    /// </summary>
    /// <param name="text">The translation text.</param>
    /// <param name="origin">The origin.</param>
    /// <param name="ambiguities">The <see cref="Ambiguities"/> is any.</param>
    public FinalTranslationValue( string text, ResourceLocator origin, IEnumerable<FinalTranslationValue>? ambiguities = null )
    {
        _text = text;
        _origin = origin;
        _ambiguities = ambiguities;
    }


    /// <summary>
    /// Returns a translation with a new <see cref="Ambiguities"/>.
    /// <para>
    /// This value is returned if the <paramref name="ambiguous"/> has the same <see cref="Text"/> as
    /// this one, or if it already belongs to this <see cref="Ambiguities"/>.
    /// </para>
    /// </summary>
    /// <param name="ambiguous">The translation that share the same key as this <see cref="Origin"/>.</param>
    /// <returns>A new resource asset or this one if <paramref name="ambiguous"/> is already known.</returns>
    public FinalTranslationValue AddAmbiguity( FinalTranslationValue ambiguous )
    {
        // We decide here that if Text are equal, there's no ambiguity to resolve even if
        // a structural ambiguity exists.
        // And if the ambiguity is already known, don't duplicate it.
        if( ambiguous.Text == Text
            || (_ambiguities != null && _ambiguities.Contains( ambiguous )) )
        {
            return this;
        }
        // Flatten the ambiguities.
        IEnumerable<FinalTranslationValue> a = _ambiguities?.Append( ambiguous ) ?? [ambiguous];
        if( ambiguous._ambiguities != null ) a = a.Concat( ambiguous._ambiguities );
        return new FinalTranslationValue( _text, _origin, a );
    }

    /// <summary>
    /// Check whether <see cref="Text"/> and <see cref="Origin"/> are equal.
    /// </summary>
    /// <param name="other">The other value.</param>
    /// <returns>Whether this <see cref="Text"/> and <see cref="Origin"/> are the equal to the other one's.</returns>
    public bool Equals( FinalTranslationValue other ) => _text == other._text && _origin == other._origin;

    /// <inheritdoc />
    public override bool Equals( object? obj ) => obj is FinalTranslationValue v && Equals( v );

    /// <summary>
    /// Overridden to return the <see cref="Text"/>.
    /// </summary>
    /// <returns>This Text.</returns>
    public override string ToString() => Text;

    /// <summary>
    /// HAsh code is based on <see cref="Text"/> and <see cref="Origin"/>.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine( Text.GetHashCode(), Origin.GetHashCode() );

    /// <summary>
    /// Tests whether <see cref="Text"/> and <see cref="Origin"/> are equal.
    /// </summary>
    /// <param name="x">The first value.</param>
    /// <param name="y">The scond value.</param>
    /// <returns>Whether the two values are equal.</returns>
    public static bool operator ==( FinalTranslationValue x, FinalTranslationValue y ) => x.Equals( y );

    /// <summary>
    /// Tests whether <see cref="Text"/> or <see cref="Origin"/> are different.
    /// </summary>
    /// <param name="x">The first value.</param>
    /// <param name="y">The scond value.</param>
    /// <returns>Whether the two values are different.</returns>
    public static bool operator !=( FinalTranslationValue x, FinalTranslationValue y ) => !x.Equals( y );
}
