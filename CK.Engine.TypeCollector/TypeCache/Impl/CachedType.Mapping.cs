using CK.Core;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace CK.Engine.TypeCollector;

sealed partial class CachedType
{
    // Computed once. A failure is memorized: the errors are logged only once.
    CachedTypeMapping? _mapping;
    bool _mappingFailed;

    public bool TryGetMapping( IActivityMonitor monitor, [NotNullWhen( true )] out CachedTypeMapping? mapping )
    {
        if( _mapping == null && !_mappingFailed )
        {
            _mapping = ComputeMapping( monitor );
            _mappingFailed = _mapping == null;
        }
        mapping = _mapping;
        return mapping != null;
    }

    CachedTypeMapping? ComputeMapping( IActivityMonitor monitor )
    {
        var t = Type;
        object? single = null;
        object? multiple = null;
        bool isNotMapped = false;
        foreach( var a in RawAttributes )
        {
            if( a is ISingleMappingAttribute ) single ??= a;
            if( a is IMultipleMappingAttribute ) multiple ??= a;
            if( a is INotMappedAttribute ) isNotMapped = true;
        }
        bool success = true;
        bool isAbstraction = t.IsInterface || (t.IsClass && t.IsAbstract && !_isDelegate);
        if( single != null && multiple != null )
        {
            monitor.Error( $"Type '{CSharpName}' cannot have both a single mapping ([{single.GetType().ToCSharpName()}]) and a multiple mapping ([{multiple.GetType().ToCSharpName()}])." );
            success = false;
        }
        if( (single != null || multiple != null) && !isAbstraction )
        {
            monitor.Error( $"Type '{CSharpName}' cannot declare a mapping cardinality ([{(single ?? multiple)!.GetType().ToCSharpName()}]): only an interface or an abstract class can." );
            success = false;
        }
        // The cardinality is inherited from the base type and the direct interfaces
        // (the other interfaces are their bases: they are already considered).
        ICachedType? singleBase = null;
        bool inheritsMultiple = false;
        if( _baseType != null ) Inherit( monitor, _baseType, ref success, ref singleBase, ref inheritsMultiple );
        foreach( var i in DirectInterfaces ) Inherit( monitor, i, ref success, ref singleBase, ref inheritsMultiple );
        if( !success ) return null;

        MappingCardinality cardinality;
        if( multiple != null )
        {
            if( singleBase != null )
            {
                monitor.Error( $"""
                    Type '{CSharpName}' cannot have a multiple mapping ([{multiple.GetType().ToCSharpName()}]): it specializes '{singleBase.CSharpName}' that has a single mapping.
                    There cannot be multiple implementations of an abstraction that has at most one.
                    """ );
                return null;
            }
            cardinality = MappingCardinality.Multiple;
        }
        else if( single != null || singleBase != null )
        {
            cardinality = MappingCardinality.Single;
        }
        else
        {
            cardinality = inheritsMultiple ? MappingCardinality.Multiple : MappingCardinality.None;
        }

        bool isKey = isAbstraction && !_isGenericTypeDefinition && cardinality != MappingCardinality.None && !isNotMapped;
        ImmutableArray<ICachedType> singleKeys = [];
        ImmutableArray<ICachedType> multipleKeys = [];
        if( t.IsClass && !t.IsAbstract && !_isDelegate && !_isGenericTypeDefinition && !isNotMapped )
        {
            ImmutableArray<ICachedType>.Builder? s = null;
            ImmutableArray<ICachedType>.Builder? m = null;
            foreach( var i in _interfaces ) AddKey( monitor, i, ref s, ref m );
            for( var b = _baseType; b != null; b = b.BaseType ) AddKey( monitor, b, ref s, ref m );
            if( s != null ) singleKeys = s.DrainToImmutable();
            if( m != null ) multipleKeys = m.DrainToImmutable();
        }
        if( cardinality == MappingCardinality.None && !isNotMapped && singleKeys.IsEmpty && multipleKeys.IsEmpty )
        {
            Throw.DebugAssert( !isKey );
            return CachedTypeMapping.Empty;
        }
        return new CachedTypeMapping( cardinality, isNotMapped, isKey, singleKeys, multipleKeys );

        static void Inherit( IActivityMonitor monitor, ICachedType b, ref bool success, ref ICachedType? singleBase, ref bool inheritsMultiple )
        {
            if( !b.TryGetMapping( monitor, out var m ) )
            {
                success = false;
            }
            else if( m.Cardinality == MappingCardinality.Single )
            {
                singleBase ??= b;
            }
            else if( m.Cardinality == MappingCardinality.Multiple )
            {
                inheritsMultiple = true;
            }
        }

        // The bases are already successfully computed (Inherit succeeded).
        static void AddKey( IActivityMonitor monitor,
                            ICachedType b,
                            ref ImmutableArray<ICachedType>.Builder? s,
                            ref ImmutableArray<ICachedType>.Builder? m )
        {
            if( b.TryGetMapping( monitor, out var bm ) && bm.IsKey )
            {
                if( bm.Cardinality == MappingCardinality.Single )
                {
                    (s ??= ImmutableArray.CreateBuilder<ICachedType>()).Add( b );
                }
                else
                {
                    (m ??= ImmutableArray.CreateBuilder<ICachedType>()).Add( b );
                }
            }
        }
    }
}
