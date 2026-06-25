using CK.Core;
using System;

namespace CK.EmbeddedResources;

/// <summary>
/// A translation definition is a <see cref="Text"/> and a <see cref="Override"/>.
/// <para>
/// Equality ignores the <see cref="Override"/>: two definitions are equal if their <see cref="Text"/> are equal.
/// </para>
/// </summary>
public readonly struct TranslationDefinition : IEquatable<TranslationDefinition>
{
    readonly string _text;
    readonly ResourceOverrideKind _override;

    /// <summary>
    /// Gets the translation text.
    /// This is empty even when <see cref="IsValid"/> is false.
    /// </summary>
    public string Text => _text ?? string.Empty;

    /// <summary>
    /// Gets whether this translation overrides an existing translation provided by another component.
    /// See <see cref="ResourceOverrideKind"/>.
    /// </summary>
    public ResourceOverrideKind Override => _override;

    /// <summary>
    /// Gets whether this value is valid.
    /// Invalid value is the <c>default</c> (<see cref="Text"/> is the empty string).
    /// </summary>
    public bool IsValid => _text != null;

    /// <summary>
    /// Initializes a new translation value.
    /// </summary>
    /// <param name="text">The translation text. When null or empty, <see cref="IsValid"/> is false.</param>
    /// <param name="overrideKind">Whether this translation overrides an existing translation provided by another component</param>
    public TranslationDefinition( string text, ResourceOverrideKind overrideKind )
    {
        _text = text;
        _override = overrideKind;
    }

    /// <summary>
    /// Check whether <see cref="Text"/> are equal. <see cref="Override"/> is ignored.
    /// </summary>
    /// <param name="other">The other value.</param>
    /// <returns>Whether this <see cref="Text"/> has the same text as the other one.</returns>
    public bool Equals( TranslationDefinition other ) => _text == other._text;

    /// <summary>
    /// Overridden to return the <see cref="Text"/>.
    /// </summary>
    /// <returns></returns>
    public override string ToString() => Text;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    public override bool Equals( object? obj ) => obj is TranslationDefinition v && Equals( v );

    public override int GetHashCode() => _text == null ? 0 : _text.GetHashCode();

    public static bool operator ==( TranslationDefinition x, TranslationDefinition y ) => x.Equals( y );

    public static bool operator !=( TranslationDefinition x, TranslationDefinition y ) => !x.Equals( y );

}
