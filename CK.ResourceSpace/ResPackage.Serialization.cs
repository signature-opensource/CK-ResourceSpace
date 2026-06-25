using CK.BinarySerialization;
using CK.Engine.TypeCollector;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;

namespace CK.Core;

[SerializationVersion(0)]
public sealed partial class ResPackage : ICKSlicedSerializable
{
    /// <summary>
    /// Deserialization constructor.
    /// </summary>
    /// <param name="d">The deserializer.</param>
    /// <param name="info">The type info.</param>
    [EditorBrowsable( EditorBrowsableState.Never )]
    public ResPackage( IBinaryDeserializer d, ITypeReadInfo info )
    {
        var r = d.Reader;
        _fullName = r.ReadString();
        _type = d.ReadNullableObject<ICachedType>();
        _defaultTargetPath = r.ReadString();
        _isGroup = r.ReadBoolean();
        _index = r.ReadInt32();
        _requires = d.ReadValue<ImmutableArray<ResPackage>>();
        _children = d.ReadValue<ImmutableArray<ResPackage>>();
        _coreData = d.ReadObject<ResCoreData>();
        _resources = d.ReadObject<ResBefore>();
        _afterResources = d.ReadObject<ResAfter>();

        // Note: we cannot check any invariant that consider other packages because
        //       we are in the middle of the serialized graph.
        if( _requires.Length == 0 )
        {
            _reachables = ImmutableHashSet<ResPackage>.Empty;
        }
        else
        {
            _reachables = d.ReadObject<IReadOnlySet<ResPackage>>();
            _requiresAggregateId = new AggregateId( r );
        }

        if( _children.Length == 0 )
        {
            _afterReachables = _reachables;
        }
        else
        {
            _afterReachables = d.ReadObject<IReadOnlySet<ResPackage>>();
            _childrenAggregateId = new AggregateId( r );
        }
    }

    /// <summary>
    /// Serialization function.
    /// </summary>
    /// <param name="s">The serializer to use.</param>
    /// <param name="o">The package to serialize.</param>
    [EditorBrowsable( EditorBrowsableState.Never )]
    public static void Write( IBinarySerializer s, in ResPackage o )
    {
        var w = s.Writer;
        w.Write( o._fullName );
        s.WriteNullableObject( o._type );
        w.Write( o._defaultTargetPath.Path );
        w.Write( o._isGroup );
        w.Write( o._index );
        s.WriteValue( o._requires );
        s.WriteValue( o._children );
        s.WriteObject( o._coreData );

        s.WriteObject( o._resources );
        s.WriteObject( o._afterResources );

        if( o._requires.Length > 0 )
        {
            s.WriteObject( o._reachables );
            o._requiresAggregateId.Write( w );
        }

        if( o._children.Length > 0 )
        {
            s.WriteObject( o._afterReachables );
            o._childrenAggregateId.Write( w );
        }
    }
}
