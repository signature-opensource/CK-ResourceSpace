using CK.Setup;
using System;

namespace CK.Engine.TypeCollector;

public sealed partial class AssemblyCache // GroupKey
{
    internal readonly struct GroupKey : IEquatable<GroupKey>
    {
        readonly BinPathConfiguration _configuration;

        public GroupKey( BinPathConfiguration configuration )
        {
            _configuration = configuration;
        }

        public bool Equals( GroupKey y )
        {
            return _configuration.Path == y._configuration.Path
                   && _configuration.DiscoverAssembliesFromPath == y._configuration.DiscoverAssembliesFromPath
                   && _configuration.Assemblies.SetEquals( y._configuration.Assemblies );
        }

        public override int GetHashCode()
        {
            int unorderedPoorHash = 0;
            foreach( var s in _configuration.Assemblies ) unorderedPoorHash ^= s.GetHashCode();
            return HashCode.Combine( _configuration.Path, _configuration.DiscoverAssembliesFromPath, unorderedPoorHash );
        }

        public override bool Equals( object? obj ) => obj is GroupKey k && Equals( k );
    }
}
