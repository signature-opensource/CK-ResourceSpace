using CK.Core;
using System.Collections.Generic;

namespace CK.EmbeddedResources;

public sealed partial class FinalTranslationSet // Serialization
{
    /// <summary>
    /// Data captured by <see cref="Serialize"/> that <see cref="Deserialize(SerializedData)"/> can
    /// use to restore a set of translations.
    /// </summary>
    /// <param name="ActiveCultures">The active cultures.</param>
    /// <param name="Translations">The root translations.</param>
    /// <param name="SubSets">The sub translation sets. Can contain null translation sets.</param>
    /// <param name="IsAmbiguous">The <see cref="FinalTranslationSet.IsAmbiguous"/>.</param>
    public sealed record SerializedData( ActiveCultureSet ActiveCultures,
                                         IReadOnlyDictionary<string, FinalTranslationValue> Translations,
                                         (IReadOnlyDictionary<string, FinalTranslationValue>?, bool)[] SubSets,
                                         bool IsAmbiguous )
    {
    }

    /// <summary>
    /// Creates the minimal data required to restore this translations set.
    /// </summary>
    /// <returns>The data to serialize.</returns>
    public SerializedData Serialize()
    {
        var sub = new (IReadOnlyDictionary<string, FinalTranslationValue>?, bool)[_subSets.Length - 1];
        for( int i = 0; i < sub.Length; i++ )
        {
            var s = _subSets[1 + i];
            if( s != null )
            {
                sub[i] = (s.Translations, s.IsAmbiguous);
            }
        }
        return new SerializedData( _activeCultures, _translations, sub, _isAmbiguous );
    }

    /// <summary>
    /// Restores a set from a <see cref="SerializedData"/>.
    /// </summary>
    /// <param name="data">The data.</param>
    /// <returns>The deserialized set.</returns>
    public static FinalTranslationSet Deserialize( SerializedData data )
    {
        var sub = new IFinalTranslationSet?[1 + data.SubSets.Length];
        for( int i = 1; i < sub.Length; i++ )
        {
            var (t, a) = data.SubSets[i - 1];
            if( t != null )
            {
                sub[i] = new SubSet( data.ActiveCultures.AllActiveCultures[i], t, a );
            }
        }
        return new FinalTranslationSet( data.ActiveCultures, data.Translations, sub, data.IsAmbiguous );
    }

}
