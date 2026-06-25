using CK.Core;
using System.Collections.Generic;

namespace CK.EmbeddedResources;

/// <summary>
/// Captures <see cref="Translations"/> definitions defined in
/// an <see cref="Origin"/> resource for a <see cref="Culture"/> .
/// </summary>
public interface ITranslationDefinitionSet
{
    /// <summary>
    /// Gets the culture of this set.
    /// </summary>
    ActiveCulture Culture { get; }

    /// <summary>
    /// Gets the origin of this set.
    /// </summary>
    ResourceLocator Origin { get; }

    /// <summary>
    /// Gets the translations.
    /// </summary>
    IReadOnlyDictionary<string, TranslationDefinition> Translations { get; }

    /// <summary>
    /// Gets the children definitions (more specific culture defintions).
    /// </summary>
    IEnumerable<ITranslationDefinitionSet> Children { get; }
}
