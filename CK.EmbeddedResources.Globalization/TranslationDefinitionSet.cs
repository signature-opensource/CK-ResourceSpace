using CK.Core;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace CK.EmbeddedResources;

/// <summary>
/// Captures a "locales/" folder: the root culture is "en" and is filled with the
/// "default.jsonc" required file.
/// <para>
/// The hierarchy is under control of the <see cref="ActiveCultures"/> tree, itself
/// under control of the <see cref="System.Globalization.CultureInfo.Parent"/> structure.
/// </para>
/// </summary>
public sealed partial class TranslationDefinitionSet : ITranslationDefinitionSet
{
    readonly ActiveCultureSet _activeCultures;
    readonly ResourceLocator _origin;
    readonly IReadOnlyDictionary<string, TranslationDefinition> _definitions;
    readonly ITranslationDefinitionSet?[] _subDefs;

    internal TranslationDefinitionSet( ActiveCultureSet activeCultures,
                                       ResourceLocator origin,
                                       IReadOnlyDictionary<string, TranslationDefinition>? translations )
    {
        _activeCultures = activeCultures;
        _origin = origin;
        _definitions = translations ?? ImmutableDictionary<string, TranslationDefinition>.Empty;
        _subDefs = new ITranslationDefinitionSet[activeCultures.Count];
        _subDefs[0] = this;
    }

    /// <summary>
    /// Gets the cultures set to which this definition is bound.
    /// </summary>
    public ActiveCultureSet ActiveCultures => _activeCultures;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, TranslationDefinition> Translations => _definitions;

    /// <inheritdoc />
    public ActiveCulture Culture => _activeCultures.Root;

    /// <inheritdoc />
    public ResourceLocator Origin => _origin;

    /// <inheritdoc />
    public IEnumerable<ITranslationDefinitionSet> Children => Culture.Children.Select( c => _subDefs[c.Index] ).Where( s => s != null )!;

    /// <summary>
    /// Creates a new initial <see cref="FinalTranslationSet"/> from this definition that is independent
    /// of any other translation definition.
    /// <para>
    /// No <see cref="TranslationDefinition.Override"/> in <see cref="Translations"/> can be <see cref="ResourceOverrideKind.Regular"/>
    /// otherwise it is an error and null is returned.
    /// </para>
    /// <para>
    /// <see cref="ResourceOverrideKind.Optional"/> are silently ignored: only <see cref="ResourceOverrideKind.None"/>
    /// and <see cref="ResourceOverrideKind.Always"/> are kept in the resulting set.
    /// </para>
    /// </summary>
    public FinalTranslationSet? ToInitialFinalSet( IActivityMonitor monitor )
    {
        var rootTranslations = CreateFinalTranslations( monitor, this, isInitialSet: true );
        bool success = rootTranslations != null;
        var subSets = new IFinalTranslationSet[_subDefs.Length];
        for( int i = 1; i < subSets.Length; ++i )
        {
            var def = _subDefs[i];
            if( def != null )
            {
                var translations = CreateFinalTranslations( monitor, def, isInitialSet: true );
                if( translations != null )
                {
                    subSets[i] = new FinalTranslationSet.SubSet( def.Culture, translations, isAmbiguous: false );
                }
                else
                {
                    success = false;
                }
            }
        }
        return success
                ? new FinalTranslationSet( _activeCultures, rootTranslations, subSets, isAmbiguous: false )
                : null;

    }

    static Dictionary<string, FinalTranslationValue>? CreateFinalTranslations( IActivityMonitor monitor,
                                                                               ITranslationDefinitionSet definition,
                                                                               bool isInitialSet )
    {
        if( isInitialSet )
        {
            var buggyOverrides = definition.Translations.Where( kv => kv.Value.Override is ResourceOverrideKind.Regular );
            if( buggyOverrides.Any() )
            {
                monitor.Error( $"""
                    Invalid initial set of translation definitions {definition.Origin}.
                    No translation can be defined as regular override ("O:"). Only optional ("O?" - that will be skipped) and always ("O!:" - that will be kept) are allowed.
                    The following resources are regular override definitions:
                    {buggyOverrides.Select( kv => kv.Key ).Concatenate()}
                    """ );
                return null;
            }
        }
        var result = new Dictionary<string, FinalTranslationValue>( definition.Translations.Count );
        foreach( var (key, def) in definition.Translations )
        {
            // Regular has trigerred the error above. Here we ignore Optional. 
            if( def.Override is ResourceOverrideKind.None or ResourceOverrideKind.Always )
            {
                result.Add( key, new FinalTranslationValue( def.Text, definition.Origin ) );
            }
        }
        return result;
    }

    /// <summary>
    /// Apply this set of definitions to a base <see cref="FinalTranslationSet"/> to produce a new
    /// final set. This can create ambiguities as well as removing ones.
    /// <para>
    /// This operation is not idempotent. When applied twice on a set, false ambiguities
    /// will be created.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="baseFinalSet">The base set to consider.</param>
    /// <returns>A final set or null on error.</returns>
    public FinalTranslationSet? Combine( IActivityMonitor monitor, FinalTranslationSet baseFinalSet )
    {
        Throw.CheckArgument( baseFinalSet.Culture == Culture );
        var (rootTranslations, isAmbiguous) = CombineTranslations( monitor, this, baseFinalSet, isRoot: true );
        bool success = rootTranslations != null;

        IFinalTranslationSet[]? cloned = null;
        for( int i = 1; i < baseFinalSet._subSets.Length; ++i )
        {
            var baseSet = baseFinalSet._subSets[i];
            var def = _subDefs[i];
            if( def == null )
            {
                if( baseSet != null )
                {
                    isAmbiguous |= baseSet.IsAmbiguous;
                }
            }
            else
            {
                // If the baseFinalSet has no definition for this specific culture, we must use
                // the closest culture that has a set defined.
                baseSet ??= baseFinalSet.FindTranslationSetOrParent( def.Culture );
                // We can now combine our definitions with the base set.
                var (t, a) = CombineTranslations( monitor, def, baseSet, isRoot: false );
                success &= t != null;
                if( success && (t != baseSet.Translations || a != baseSet.IsAmbiguous) )
                {
                    Throw.DebugAssert( t != null );
                    cloned ??= baseFinalSet.CloneSubSets();
                    cloned[i] = new FinalTranslationSet.SubSet( def.Culture, t, a );
                    isAmbiguous |= a;
                }
            }
        }
        if( success )
        {
            if( cloned == null && rootTranslations == baseFinalSet.Translations )
            {
                return baseFinalSet;
            }
            return new FinalTranslationSet( _activeCultures, rootTranslations, cloned ?? baseFinalSet.CloneSubSets(), isAmbiguous );
        }
        return null;
    }

    static (IReadOnlyDictionary<string, FinalTranslationValue>?,bool) CombineTranslations( IActivityMonitor monitor,
                                                                                           ITranslationDefinitionSet definition,
                                                                                           IFinalTranslationSet baseSet,
                                                                                           bool isRoot )
    {
        bool success = true;
        bool isAmbiguous = !isRoot && baseSet.IsAmbiguous;
        bool mustRecomputeAmbiguities = isRoot;
        var result = baseSet.Translations;
        if( definition.Translations.Count > 0 )
        {
            bool changed = false;
            var newOne = new Dictionary<string, FinalTranslationValue>( baseSet.Translations );
            foreach( var (key, def) in definition.Translations )
            {
                bool fromInheritance = false;
                if( !newOne.TryGetValue( key, out var exists ) )
                {
                    var p = baseSet.ClosestParent;
                    while( p != null && !p.Translations.TryGetValue( key, out exists ) )
                    {
                        p = p.ClosestParent;
                    }
                    fromInheritance = true;
                }
                if( exists.IsValid )
                {
                    if( def.Override is ResourceOverrideKind.None )
                    {
                        monitor.Error( $"""
                                    Key '{key}' in {definition.Origin} overides an existing value:
                                    This key is already defined by '{exists.Origin}' with the value:
                                    {exists.Text}
                                    The new value would be:
                                    {def.Text}
                                    Use Override prefix ('O:{key}') to allow this.
                                    """ );
                        success = false;
                    }
                    else
                    {
                        // Whether it is a "O", "?O" or "!O" we don't care here as we override.
                        // We warn for a useless (same) text, except if this override resolves an ambiguity.
                        bool hasAmbiguities = exists.Ambiguities != null;
                        if( !hasAmbiguities && def.Text == exists.Text )
                        {
                            monitor.Warn( $"""
                                       Useless override 'O:{key}' in {definition.Origin}: it has the same value as the one from {exists.Origin}:
                                       {def.Text}
                                       """ );
                        }
                        else
                        {
                            // Override (clears any ambiguity).
                            newOne[key] = new FinalTranslationValue( def.Text, definition.Origin );
                            mustRecomputeAmbiguities = hasAmbiguities && !fromInheritance;
                            changed = true;
                        }
                    }
                }
                else
                {
                    if( def.Override is ResourceOverrideKind.Regular )
                    {
                        monitor.Warn( $"Invalid override 'O:{key}' in {definition.Origin}: this key is not defined by any reachable translation sets." );
                    }
                    else
                    {
                        if( def.Override != ResourceOverrideKind.Optional )
                        {
                            newOne.Add( key, new FinalTranslationValue( def.Text, definition.Origin ) );
                            changed = true;
                        }
                    }
                }
            }
            if( changed ) result = newOne;
        }
        if( success )
        {
            if( mustRecomputeAmbiguities )
            {
                isAmbiguous = result.Values.Any( v => v.Ambiguities != null );
            }
            return (result,isAmbiguous);
        }
        return default;
    }



}
