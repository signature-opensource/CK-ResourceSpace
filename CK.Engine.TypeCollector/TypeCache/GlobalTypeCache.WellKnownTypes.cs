using CK.Core;
using System.Collections.Generic;

namespace CK.Engine.TypeCollector;

public sealed partial class GlobalTypeCache
{
    /// <summary>
    /// Captures a set of well-kbown types.
    /// </summary>
    public sealed class WellKnownTypes
    {
        readonly GlobalTypeCache _cache;
        ICachedType? _void;
        ICachedType? _object;
        ICachedType _delegate;
        ICachedType? _multiCastDelegate;
        ICachedType? _iActivityMonitor;
        ICachedType? _iParallelLogger;
        ICachedType? _iRealObject;
        ICachedType? _iPoco;
        ICachedType? _iAutoService;
        ICachedType? _task;
        ICachedType? _genericTaskDefinition;
        ICachedType? _valueTask;
        ICachedType? _genericValueTaskDefinition;
        ICachedType? _iEnumerable;
        ICachedType? _genericIEnumerableDefinition;
        ICachedType? _genericIListDefinition;
        ICachedType? _genericIReadOnlyListDefinition;
        ICachedType? _genericISetDefinition;
        ICachedType? _genericIReadOnlySetDefinition;
        ICachedType? _genericIDictionaryDefinition;
        ICachedType? _genericIReadOnlyDictionaryDefinition;

        internal WellKnownTypes( GlobalTypeCache cache, CachedType del )
        {
            _cache = cache;
            _delegate = del;
        }

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

        public ICachedType Void => _void ??= _cache.Get( typeof( void ) );
        public ICachedType Object => _object ??= _cache.Get( typeof( object ) );
        public ICachedType Delegate => _delegate;
        public ICachedType MulticastDelegate => _multiCastDelegate ??= _cache.Get( typeof( System.MulticastDelegate ) );
        public ICachedType IActivityMonitor => _iActivityMonitor ??= _cache.Get( typeof( IActivityMonitor ) );
        public ICachedType IParallelLogger => _iParallelLogger ??= _cache.Get( typeof( IParallelLogger ) );
        public ICachedType IRealObject => _iRealObject ??= _cache.Get( typeof( IRealObject ) );
        public ICachedType IPoco => _iPoco ??= _cache.Get( typeof( IPoco ) );
        public ICachedType IAutoService => _iAutoService ??= _cache.Get( typeof( IAutoService ) );
        public ICachedType Task => _task ??= _cache.Get( typeof( System.Threading.Tasks.Task ) );
        public ICachedType GenericTaskDefinition => _genericTaskDefinition ??= _cache.Get( typeof( System.Threading.Tasks.Task<> ) );
        public ICachedType ValueTask => _valueTask ??= _cache.Get( typeof( System.Threading.Tasks.ValueTask ) );
        public ICachedType GenericValueTaskDefinition => _genericValueTaskDefinition ??= _cache.Get( typeof( System.Threading.Tasks.ValueTask<> ) );
        public ICachedType IEnumerable => _iEnumerable ??= _cache.Get( typeof( System.Collections.IEnumerable ) );
        public ICachedType GenericIEnumerableDefinition => _genericIEnumerableDefinition ??= _cache.Get( typeof( IEnumerable<> ) );
        public ICachedType GenericIListDefinition => _genericIListDefinition ??= _cache.Get( typeof( IList<> ) );
        public ICachedType GenericIReadOnlyListDefinition => _genericIReadOnlyListDefinition ??= _cache.Get( typeof( IReadOnlyList<> ) );
        public ICachedType GenericISetDefinition => _genericISetDefinition ??= _cache.Get( typeof( ISet<> ) );
        public ICachedType GenericIReadOnlySetDefinition => _genericIReadOnlySetDefinition ??= _cache.Get( typeof( IReadOnlySet<> ) );
        public ICachedType GenericIDictionaryDefinition => _genericIDictionaryDefinition ??= _cache.Get( typeof( IDictionary<,> ) );
        public ICachedType GenericIReadOnlyDictionaryDefinition => _genericIReadOnlyDictionaryDefinition ??= _cache.Get( typeof( IReadOnlyDictionary<,> ) );
    }
}
