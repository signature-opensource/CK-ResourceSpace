using CK.Core;
using System.Collections.Generic;
using System.Linq;

namespace CK.EmbeddedResources;

public sealed partial class FinalTranslationSet // SubSets
{
    internal sealed class SubSet : IFinalTranslationSet
    {
        internal FinalTranslationSet? _root;
        readonly IReadOnlyDictionary<string, FinalTranslationValue> _translations;
        readonly ActiveCulture _culture;
        readonly bool _isAmbiguous;

        public SubSet( IFinalTranslationSet o )
            : this( o.Culture, o.Translations, o.IsAmbiguous )
        {
        }

        public SubSet( ActiveCulture culture,
                       IReadOnlyDictionary<string, FinalTranslationValue> translations,
                       bool isAmbiguous )
        {
            Throw.DebugAssert( culture.Index > 0 );
            _translations = translations;
            _isAmbiguous = isAmbiguous;
            _culture = culture;
        }

        void IFinalTranslationSet.LocalImplementationOnly() { }

        public IReadOnlyDictionary<string, FinalTranslationValue> Translations => _translations;

        public bool IsAmbiguous => _isAmbiguous;

        public ActiveCulture Culture => _culture;

        public IFinalTranslationSet ClosestParent
        {
            get
            {
                Throw.DebugAssert( _root != null );
                Throw.DebugAssert( "We are NOT on the root ActiveCulture.", _culture.Parent != null );
                return _root.DoFindTranslationSetOrParent( _culture.Parent );
            }
        }

        public IEnumerable<IFinalTranslationSet> Children
        {
            get
            {
                Throw.DebugAssert( _root != null );
                return _culture.Children.Select( c => _root._subSets[c.Index] ).Where( s => s != null )!;
            }
        }

        public IEnumerable<KeyValuePair<string, FinalTranslationValue>> RootPropagatedTranslations
        {
            get
            {
                // This is not obvious but should be optimal:
                // - No allocation for the direct children of the root.
                // - A single HashSet hidden by closure for deeper children.
                IEnumerable<KeyValuePair<string, FinalTranslationValue>> source = _translations;
                var parent = ClosestParent;
                source = source.Concat( parent.Translations.Where( kv => !_translations.ContainsKey( kv.Key ) ) );
                // More than one level?
                var grandParent = parent.ClosestParent;
                if( grandParent != null )
                {
                    var dedup = new HashSet<string>( _translations.Keys.Concat( parent.Translations.Keys ) );
                    do
                    {
                        source = source.Concat( grandParent.Translations.Where( kv => dedup.Add( kv.Key ) ) );
                        grandParent = grandParent.ClosestParent;
                    }
                    while( grandParent != null );
                }
                return source;
            }
        }

        public override string ToString() => _culture.Culture.Name;
    }

    internal IFinalTranslationSet[] CloneSubSets()
    {
        var s = new IFinalTranslationSet[_subSets.Length];
        for( int i = 1; i < _subSets.Length; ++i )
        {
            var o = _subSets[i];
            if( o != null )
            {
                s[i] = new SubSet( o );
            }
        }
        return s;
    }
}
