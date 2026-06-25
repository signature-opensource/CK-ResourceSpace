using CK.BinarySerialization;
using CK.EmbeddedResources;
using CK.Engine.TypeCollector;
using System;

namespace CK.Core;

public sealed partial class ResSpace
{
    // Handles ICachedType that is not serializable and ResourceContainerWrapper, ResourceLocator and ResourceFolder
    // binary serialization that are not implemented in CK.EmbeddedResources assembly to limit it only to
    // simple serialization.
    //
    // The ResourceContainerWrapper needs to write its inner container. We use here a surrogate approach: the
    // CodeGenResourceContainer or EmptyResourceContainer is written as a CodeGenResourceContainer (only these
    // ones are supported).
    //
    // ResourceLocator and ResourceFolder are simple drivers.
    //
    // ICachedType serializer writes the Type and ICachedType deserializer reads the Type and obtain the ICachedType
    // from the GlobalTypeCache that must be registered in the deserializer context services. 
    //
    sealed class ResourceLocatorSerializer : StaticValueTypeSerializer<ResourceLocator>
    {
        public override string DriverName => "ResourceLocator";

        public override int SerializationVersion => 0;

        public static void Write( IBinarySerializer s, in ResourceLocator o )
        {
            s.WriteNullableObject( o.Container );
            s.Writer.WriteNullableString( o.FullResourceName );
        }
    }

    sealed class ResourceLocatorDeserializer : ValueTypeDeserializer<ResourceLocator>
    {
        protected override ResourceLocator ReadInstance( IBinaryDeserializer d, ITypeReadInfo readInfo )
        {
            return ResourceLocator.UnsafeCreate( d.ReadNullableObject<IResourceContainer>()!, d.Reader.ReadNullableString()! );
        }
    }

    sealed class ResourceFolderSerializer : StaticValueTypeSerializer<ResourceFolder>
    {
        public override string DriverName => "ResourceFolder";

        public override int SerializationVersion => 0;

        public static void Write( IBinarySerializer s, in ResourceFolder o )
        {
            s.WriteNullableObject( o.Container );
            s.Writer.WriteNullableString( o.FullFolderName );
        }
    }

    sealed class ResourceFolderDeserializer : ValueTypeDeserializer<ResourceFolder>
    {
        protected override ResourceFolder ReadInstance( IBinaryDeserializer d, ITypeReadInfo readInfo )
        {
            return ResourceFolder.UnsafeCreate( d.ReadNullableObject<IResourceContainer>()!, d.Reader.ReadNullableString()! );
        }
    }

    sealed class NormalizedPathSerializer : StaticValueTypeSerializer<NormalizedPath>
    {
        public override string DriverName => "NormalizedPath";

        public override int SerializationVersion => 0;

        public static void Write( IBinarySerializer s, in NormalizedPath o )
        {
            s.Writer.Write( o.Path );
        }
    }

    sealed class NormalizedPathDeserializer : ValueTypeDeserializer<NormalizedPath>
    {
        protected override NormalizedPath ReadInstance( IBinaryDeserializer d, ITypeReadInfo readInfo )
        {
            return d.Reader.ReadString();
        }
    }

    sealed class CachedTypeResolver : ISerializerResolver, IDeserializerResolver
    {
        const string _driverName = "ICachedType-Family";

        readonly ISerializationDriver _serializer = new CachedTypeSerializer();
        readonly IDeserializationDriver _deserializer = new CachedTypeDeserializer();

        public ISerializationDriver? TryFindDriver( BinarySerializerContext context, Type t )
        {
            return typeof( ICachedType ).IsAssignableFrom( t )
                    ? _serializer
                    : null;
        }

        public IDeserializationDriver? TryFindDriver( ref DeserializerResolverArg info )
        {
            return info.DriverName == _driverName
                    ? _deserializer
                    : null;
        }

        sealed class CachedTypeSerializer : ReferenceTypeSerializer<ICachedType>
        {
            public override string DriverName => _driverName;

            public override int SerializationVersion => 0;

            protected override void Write( IBinarySerializer s, in ICachedType o )
            {
                s.WriteObject( o.Type );
            }
        }

        sealed class CachedTypeDeserializer : ReferenceTypeDeserializer<ICachedType>
        {
            protected override void ReadInstance( ref RefReader r )
            {
                var c = r.DangerousDeserializer.Context.Services.GetService<GlobalTypeCache>( throwOnNull: true );
                var o = c.Get( r.DangerousDeserializer.ReadObject<Type>() );
                r.SetInstance( o );
            }
        }
    }

    static ResSpace()
    {
        BinarySerializer.DefaultSharedContext.AddSerializationDriver( typeof( NormalizedPath ), new NormalizedPathSerializer() );
        BinaryDeserializer.DefaultSharedContext.AddDeserializerDriver( new NormalizedPathDeserializer() );

        BinarySerializer.DefaultSharedContext.AddSerializationDriver( typeof( ResourceLocator ), new ResourceLocatorSerializer() );
        BinaryDeserializer.DefaultSharedContext.AddDeserializerDriver( new ResourceLocatorDeserializer() );

        BinarySerializer.DefaultSharedContext.AddSerializationDriver( typeof( ResourceFolder ), new ResourceFolderSerializer() );
        BinaryDeserializer.DefaultSharedContext.AddDeserializerDriver( new ResourceFolderDeserializer() );

        var cachedTypeResolver = new CachedTypeResolver();
        BinarySerializer.DefaultSharedContext.AddResolver( cachedTypeResolver );
        BinaryDeserializer.DefaultSharedContext.AddResolver( cachedTypeResolver );

        // This should be something like:
        // 
        // BinarySerializer.SurrogateResolver.Default.Register<ResourceContainerWrapper,IResourceContainer>( w => w.InnerContainer ) );
        //
        // Unfortunately, true surrogates are not supported.
        //
        BinarySerializer.DefaultSharedContext.AddSerializationDriver( typeof( ResourceContainerWrapper ), new AsInnerCodeContainer() );
    }

    sealed class AsInnerCodeContainer : ReferenceTypeSerializer<ResourceContainerWrapper>, ISerializationDriverTypeRewriter
    {
        public override string DriverName => "VersionedBinarySerializable";

        public override int SerializationVersion => 0;

        public Type GetTypeToWrite( Type type ) => typeof( CodeGenResourceContainer );

        protected override void Write( IBinarySerializer s, in ResourceContainerWrapper o )
        {
            if( o.InnerContainer is CodeGenResourceContainer code )
            {
                code.WriteData( s.Writer );
            }
            else if( o.InnerContainer is EmptyResourceContainer empty )
            {
                s.Writer.Write( empty.DisplayName );
                s.Writer.Write( 0 );
            }
            else
            {
                Throw.NotSupportedException(
                    "Due to binary serialization limitation, ResourceContainerWrapper can only wrap " +
                    "a CodeGenResourceContainer or a EmptyResourceContainer." );
            }
        }
    }

}
