using CK.Core;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CK.EmbeddedResources;

/// <summary>
/// Translations associated to a <see cref="ActiveCultureSet"/>.
/// Only empty sets can be created directly, actual sets are read from resources:
/// see <see cref="ResourceContainerGlobalizationExtension.LoadTranslations(IResourceContainer, IActivityMonitor, ActiveCultureSet, out TranslationDefinitionSet?, string, bool)"/>.
/// <para>
/// Serialization is externalized by <see cref="Serialize"/>/<see cref="Deserialize(SerializedData)"/> and has to be
/// done explicitly.
/// </para>
/// </summary>
public sealed partial class FinalTranslationSet : IFinalTranslationSet
{
    readonly IReadOnlyDictionary<string, FinalTranslationValue> _translations;
    readonly ActiveCultureSet _activeCultures;
    internal readonly IFinalTranslationSet?[] _subSets;
    readonly bool _isAmbiguous;

    internal FinalTranslationSet( ActiveCultureSet activeCultures,
                                  IReadOnlyDictionary<string, FinalTranslationValue>? translations,
                                  IFinalTranslationSet?[] subSets,
                                  bool isAmbiguous )
    {
        _activeCultures = activeCultures;
        _translations = translations ?? ImmutableDictionary<string, FinalTranslationValue>.Empty;
        _subSets = subSets;
        _isAmbiguous = isAmbiguous;
        subSets[0] = this;
        for( int i = 1; i < subSets.Length; i++ )
        {
            var sub = subSets[i];
            if( sub != null )
            {
                Throw.DebugAssert( sub is SubSet s && s._root == null );
                ((SubSet)sub)._root = this;
            }
        }
    }

    void IFinalTranslationSet.LocalImplementationOnly() { }

    /// <summary>
    /// Initializes a new empty final translation set.
    /// </summary>
    /// <param name="activeCultures">The active culture set.</param>
    public FinalTranslationSet( ActiveCultureSet activeCultures )
    {
        _activeCultures = activeCultures;
        _translations = ImmutableDictionary<string, FinalTranslationValue>.Empty;
        _subSets = new IFinalTranslationSet[activeCultures.Count];
        _subSets[0] = this;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, FinalTranslationValue> Translations => _translations;

    /// <inheritdoc />
    public ActiveCulture Culture => _activeCultures.Root;

    /// <inheritdoc />
    public IFinalTranslationSet? ClosestParent => null;

    /// <inheritdoc />
    public IEnumerable<IFinalTranslationSet> Children => _activeCultures.Root.Children.Select( c => _subSets[c.Index] ).Where( s => s != null )!;

    /// <inheritdoc />
    public bool IsAmbiguous => _isAmbiguous;

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, FinalTranslationValue>> RootPropagatedTranslations => _translations;

    /// <summary>
    /// Gets all the existing ambiguities.
    /// </summary>
    public IEnumerable<KeyValuePair<string, FinalTranslationValue>> Ambiguities => _subSets.Where( s => s != null )
                                                                                          .SelectMany( s => s!.Translations
                                                                                                              .Where( kv => kv.Value.Ambiguities != null ) );

    /// <summary>
    /// Gets all the translation sets (including this root).
    /// Order is irrelevant and there may be less sets than <see cref="ActiveCultureSet.Count"/>.
    /// Nullable sets can be obtained by <see cref="FindTranslationSet(ActiveCulture)"/>.
    /// </summary>
    public IEnumerable<IFinalTranslationSet> AllTranslationSets => _subSets.Where( s => s != null )!;

    /// <summary>
    /// Finds the translation set for an active culture.
    /// It is null if it has no translation.
    /// </summary>
    /// <param name="c">The active culture. Must be from the same set as this active <see cref="Culture"/>.</param>
    /// <returns>The set or null if its creation was useless.</returns>
    public IFinalTranslationSet? FindTranslationSet( ActiveCulture c  )
    {
        Throw.CheckArgument( c.ActiveCultures == _activeCultures );
        return _subSets[c.Index];
    }

    /// <summary>
    /// Finds the translation set for an active culture, locating the first non null set associated
    /// to the <see cref="ActiveCulture.Parent"/> chain.
    /// </summary>
    /// <param name="c">The active culture. Must be from the same set as this active <see cref="Culture"/>.</param>
    /// <returns>The set or a parent set.</returns>
    public IFinalTranslationSet FindTranslationSetOrParent( ActiveCulture c  )
    {
        Throw.CheckArgument( c.ActiveCultures == _activeCultures );
        return DoFindTranslationSetOrParent( c );
    }

    IFinalTranslationSet DoFindTranslationSetOrParent( ActiveCulture c )
    {
        Throw.DebugAssert( c.ActiveCultures == _activeCultures );
        var s = _subSets[c.Index];
        while( s == null )
        {
            Throw.DebugAssert( "Since the _subSets[0] is necessarily defined.", c.Parent != null );
            c = c.Parent;
            s = _subSets[c.Index];
        }
        return s;
    }

    /// <summary>
    /// Finds the translation set for a culture.
    /// It is null if this culture doesn't belong to this set of active cultures or
    /// if it has no translation.
    /// </summary>
    /// <param name="c">The active culture. Must be from the same set as this active <see cref="Culture"/>.</param>
    /// <returns>The set or null if its creation was useless.</returns>
    public IFinalTranslationSet? FindTranslationSet( NormalizedCultureInfo c )
    {
        var aC = _activeCultures.Get( c );
        return aC != null ? _subSets[aC.Index] : null;
    }

    /// <summary>
    /// Finds the translation set for a culture or the first non null set associated
    /// to the <see cref="ActiveCulture.Parent"/> chain.
    /// It is null if this culture doesn't belong to this set of active cultures.
    /// </summary>
    /// <param name="c">The active culture. Must be from the same set as this active <see cref="Culture"/>.</param>
    /// <returns>The set or null if its creation was useless.</returns>
    public IFinalTranslationSet? FindTranslationSetOrParent( NormalizedCultureInfo c )
    {
        var aC = _activeCultures.Get( c );
        return aC != null ? DoFindTranslationSetOrParent( aC ) : null;
    }

    /// <summary>
    /// Aggregate this set with another one. Even if both sets are not <see cref="IsAmbiguous"/>,
    /// if a common key is mapped to different text, the result will be ambiguous.
    /// </summary>
    /// <param name="other">The other set to aggregate.</param>
    /// <returns>The aggregated set.</returns>
    public FinalTranslationSet Aggregate( FinalTranslationSet other )
    {
        Throw.CheckArgument( Culture == other.Culture );
        var translations = AggregateTranslations( this, other, out bool isAmbiguous );

        bool clonedChanged = false;
        var cloned = CloneSubSets();
        for( int i = 1; i < cloned.Length; ++i )
        {
            var mine = _subSets[i];
            var their = other._subSets[i];
            if( mine == null )
            {
                if( their != null )
                {
                    cloned[i] = new SubSet( their );
                    clonedChanged = true;
                    isAmbiguous |= their.IsAmbiguous;
                }
            }
            else
            {
                if( their != null )
                {
                    var agg = AggregateTranslations( mine, their, out var aggAmbiguous );
                    if( agg != mine.Translations )
                    {
                        clonedChanged = true;
                        cloned[i] = new SubSet( mine.Culture, agg, aggAmbiguous );
                        isAmbiguous |= aggAmbiguous;
                    }
                }
            }
        }

        if( translations == _translations && !clonedChanged )
        {
            Throw.DebugAssert( isAmbiguous == _isAmbiguous );
            return this;
        }
        return new FinalTranslationSet( _activeCultures, translations, cloned, isAmbiguous );
    }

    static IReadOnlyDictionary<string, FinalTranslationValue> AggregateTranslations( IFinalTranslationSet set1,
                                                                                     IFinalTranslationSet set2,
                                                                                     out bool isAmbiguous )
    {
        IReadOnlyDictionary<string, FinalTranslationValue> result;
        if( set1.Translations.Count > 0 )
        {
            if( set2.Translations.Count > 0 )
            {
                // Iterate on the smallest to add/update into the biggest.
                var (s1, s2) = (set1, set2);
                if( set1.Translations.Count > set2.Translations.Count ) (s1, s2) = (s2, s1);
                var candidate = new Dictionary<string, FinalTranslationValue>( s2.Translations );
                bool changed = false;
                isAmbiguous = s2.IsAmbiguous;
                foreach( var (key, t1) in s1.Translations )
                {
                    if( candidate.TryGetValue( key, out var t2 ) )
                    {
                        var f = t1.AddAmbiguity( t2 );
                        if( f.Ambiguities != t1.Ambiguities )
                        {
                            isAmbiguous |= f.Ambiguities != null;
                            candidate[key] = f;
                            changed = true;
                        }
                    }
                    else
                    {
                        candidate.Add( key, t1 );
                        changed = true;
                    }
                }
                result = changed ? candidate : s2.Translations;
            }
            else
            {
                result = set1.Translations;
                isAmbiguous = set1.IsAmbiguous;
            }
        }
        else
        {
            result = set2.Translations;
            isAmbiguous = set2.IsAmbiguous;
        }
        return result;
    }

    /// <summary>
    /// Returns the all the active culture names.
    /// </summary>
    /// <returns>Teh active culture names.</returns>
    public override string ToString() => _activeCultures.ToString();
}
