using CK.BinarySerialization;
using CK.EmbeddedResources;
using System.Collections.Generic;
using System.ComponentModel;

namespace CK.Core;

public sealed partial class ResPackage
{
    [SerializationVersion( 0 )]
    sealed class ResAfter : IResPackageResources, IResPackageData, ICKSlicedSerializable
    {
        ResData _data;

        public ResAfter( ResPackage package, IResourceContainer resources, int index )
        {
            _data = new ResData( package, resources, index );
            Throw.DebugAssert( IsCodeResources == (package.FullName == "<Code>") );
        }

        [EditorBrowsable( EditorBrowsableState.Never )]
        public ResAfter( IBinaryDeserializer d, ITypeReadInfo info )
        {
            _data = new ResData( d );
            Throw.DebugAssert( IsCodeResources == (_data._package.FullName == "<Code>") );
        }

        [EditorBrowsable( EditorBrowsableState.Never )]
        public static void Write( IBinarySerializer s, in ResAfter o )
        {
            o._data.Write( s );
        }

        void IResPackageData.SetLocalIndex( int localIndex ) => _data._localIndex = localIndex;

        public bool IsAfter => true;

        public IReadOnlySet<ResPackage> Reachables => _data._package._afterReachables;

        public int Index => _data._index;

        public int LocalIndex => _data._localIndex;

        public bool IsCodeResources => _data._index == 1;

        public bool IsAppResources => false;

        public IResourceContainer Resources => _data._resources;

        public ResPackage Package => _data._package;

        public string? LocalPath => _data._localPath;

        public override string ToString() => $"After {_data._package}";
    }
}
