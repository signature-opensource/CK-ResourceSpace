using System.Collections.Generic;
using System.Reflection;

namespace CK.Engine.TypeCollector;

public sealed partial class AssemblyCache // Result
{
    /// <summary>
    /// Result of a <see cref="Run"/>: exposes the <see cref="AssemblyCache"/> and the <see cref="BinPathGroups"/>.
    /// <para>
    /// New assembly can be registered by calling <see cref="FindOrCreate(Assembly)"/>.
    /// </para>
    /// </summary>
    public sealed class Result
    {
        readonly AssemblyCache _cache;
        readonly GlobalTypeCache _typeCache;
        readonly IReadOnlyCollection<BinPathGroup> _binPaths;
        readonly bool _success;

        internal Result( bool success, AssemblyCache cache, GlobalTypeCache typeCache, IReadOnlyCollection<BinPathGroup> binPaths )
        {
            _success = success;
            _cache = cache;
            _typeCache = typeCache;
            _binPaths = binPaths;
        }

        /// <summary>
        /// Gets whether all <see cref="BinPathGroups"/> are on success.
        /// </summary>
        public bool Success => _success;

        /// <summary>
        /// Gets the assembly cache.
        /// </summary>
        public AssemblyCache AssemblyCache => _cache;

        /// <summary>
        /// Gets the global type cache.
        /// </summary>
        public GlobalTypeCache TypeCache => _typeCache;

        /// <summary>
        /// Gets the <see cref="BinPathGroup"/> with their similar assembly related <see cref="BinPathGroup.Configurations"/>.
        /// </summary>
        public IReadOnlyCollection<BinPathGroup> BinPathGroups => _binPaths;

    }
}
