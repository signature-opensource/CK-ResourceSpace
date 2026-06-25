using CK.BinarySerialization;
using CK.EmbeddedResources;

namespace CK.Core;

public sealed partial class ResPackage
{
    internal interface IResPackageData
    {
        void SetLocalIndex( int localIndex );
    }

    /// <summary>
    /// Using a struct to hold the data of Before and AfterRes avoids a base
    /// class while centralizing code. 
    /// </summary>
    struct ResData
    {
        internal readonly ResPackage _package;
        internal readonly IResourceContainer _resources;
        internal readonly string? _localPath;
        internal readonly int _index;
        // Set by ResSpaceDataBuilder.Build().
        // -1 if _localPath is null.
        internal int _localIndex;

        public ResData( ResPackage package, IResourceContainer resources, int index )
        {
            _package = package;
            _resources = resources;
            _index = index;
            _localPath = GetLocalPath( resources );
            _localIndex = -1;
        }

        public ResData( IBinaryDeserializer d )
        {
            _package = d.ReadObject<ResPackage>();
            _resources = d.ReadObject<IResourceContainer>();
            _index = d.Reader.ReadNonNegativeSmallInt32();
            _localPath = GetLocalPath( _resources );
            _localIndex = d.Reader.ReadSmallInt32();
        }

        public void Write( IBinarySerializer s )
        {
            s.WriteObject( _package );
            s.WriteObject( _resources );
            s.Writer.WriteNonNegativeSmallInt32( _index );
            s.Writer.WriteSmallInt32( _localIndex );
        }

        static string? GetLocalPath( IResourceContainer resources ) => resources is StoreContainer c
                                                                        ? c.LocalPath
                                                                        : resources is FileSystemResourceContainer f
                                                                          && f.HasLocalFilePathSupport
                                                                            ? f.ResourcePrefix
                                                                            : null;
    }
}
