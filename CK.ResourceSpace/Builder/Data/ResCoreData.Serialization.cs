using CK.BinarySerialization;
using CK.EmbeddedResources;
using CK.Engine.TypeCollector;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;

namespace CK.Core;

[SerializationVersion(0)]
public sealed partial class ResCoreData : ICKSlicedSerializable
{
    /// <summary>
    /// Deserialization constructor.
    /// </summary>
    /// <param name="d">The deserializer.</param>
    /// <param name="info">The type info.</param>
    [EditorBrowsable( EditorBrowsableState.Never )]
    public ResCoreData( IBinaryDeserializer d, ITypeReadInfo info )
    {
        _localPackages = d.ReadValue<ImmutableArray<ResPackage>>();
        _packages = d.ReadValue<ImmutableArray<ResPackage>>();
        _allPackageResources = d.ReadValue<ImmutableArray<IResPackageResources>>();
        _localPackageResources = d.ReadValue<ImmutableArray<IResPackageResources>>();
        _packageIndex = d.ReadObject<IReadOnlyDictionary<object, ResPackage>>();
        _typeCache = d.Context.Services.GetService<GlobalTypeCache>( throwOnNull: true );
        _resourceIndex = d.ReadObject<IReadOnlyDictionary<IResourceContainer, IResPackageResources>>();
        _codeHandledResources = d.ReadObject<IReadOnlySet<ResourceLocator>>();
        _excludedOptionalResourcePaths = d.ReadValue<ImmutableArray<string>>();
        _codePackage = _packages[0];
        _appPackage = _packages[^1];

        var r = d.Reader;
        _resPackageDataCache = LiveSpaceDataCache.Read( r, _packages, _allPackageResources );
        _watchRoot = r.ReadNullableString();
        _liveStatePath = r.ReadString();
    }

    /// <summary>
    /// Serialization function.
    /// </summary>
    /// <param name="s">The serializer to use.</param>
    /// <param name="o">The space data to serialize.</param>
    [EditorBrowsable( EditorBrowsableState.Never )]
    public static void Write( IBinarySerializer s, in ResCoreData o )
    {
        s.WriteValue( o._localPackages );
        s.WriteValue( o._packages );
        s.WriteValue( o._allPackageResources );
        s.WriteValue( o._localPackageResources );
        s.WriteObject( o._packageIndex );
        s.WriteObject( o._resourceIndex );
        s.WriteObject( o._codeHandledResources );
        s.WriteValue( o._excludedOptionalResourcePaths );

        ICKBinaryWriter w = s.Writer;
        ((ResCoreDataCache)o._resPackageDataCache).Write( w );
        w.WriteNullableString( o._watchRoot );
        w.Write( o._liveStatePath );
    }
}
