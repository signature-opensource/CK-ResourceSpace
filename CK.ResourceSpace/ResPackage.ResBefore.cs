using CK.BinarySerialization;
using CK.EmbeddedResources;
using System.Collections.Generic;
using System.ComponentModel;

namespace CK.Core;

public sealed partial class ResPackage
{
    [SerializationVersion(0)]
    sealed class ResBefore : IResPackageResources, IResPackageData, ICKSlicedSerializable
    {
        ResData _data;

        public ResBefore( ResPackage package, IResourceContainer resources, int index )
        {
            _data = new ResData( package, resources, index );
        }


        [EditorBrowsable(EditorBrowsableState.Never)]
        public ResBefore( IBinaryDeserializer d, ITypeReadInfo info )
        {
            _data = new ResData( d );
        }

        [EditorBrowsable( EditorBrowsableState.Never )]
        public static void Write( IBinarySerializer s, in ResBefore o )
        {
            o._data.Write( s );
        }

        void IResPackageData.SetLocalIndex( int localIndex ) => _data._localIndex = localIndex;

        public bool IsAfter => false;

        public IReadOnlySet<ResPackage> Reachables => _data._package._reachables;

        public int Index => _data._index;

        public int LocalIndex => _data._localIndex;

        public bool IsCodeResources => false;

        public bool IsAppResources => _data._package.IsAppPackage;

        public IResourceContainer Resources => _data._resources;

        public ResPackage Package => _data._package;

        public string? LocalPath => _data._localPath;

        public override string ToString() => $"Before {_data._package}";

    }
}
