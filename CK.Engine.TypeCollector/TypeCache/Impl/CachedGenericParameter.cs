using CK.Core;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CK.Engine.TypeCollector;

sealed class CachedGenericParameter : ICachedType
{
    readonly Type _parameter;
    readonly GlobalTypeCache _typeCache;
    readonly CachedAssembly _assembly;
    ImmutableArray<CustomAttributeData> _customAttributes;
    ImmutableArray<object> _attributes;
    ICachedType? _declaringType;

    public CachedGenericParameter( GlobalTypeCache typeCache, Type type, CachedAssembly assembly )
    {
        Throw.DebugAssert( type.DeclaringType != null );
        _typeCache = typeCache;
        _parameter = type;
        _assembly = assembly;
    }

    public Type Type => _parameter;

    public string CSharpName => _parameter.Name;

    public CachedAssembly Assembly => _assembly;

    public bool? IsNullable => null;

    public ICachedType Nullable => this;

    public ICachedType NonNullable => this;

    public ImmutableArray<ICachedType> Interfaces => ImmutableArray<ICachedType>.Empty;

    public ImmutableArray<ICachedType> DirectInterfaces => ImmutableArray<ICachedType>.Empty;

    public ICachedType? BaseType => null;

    public ImmutableArray<ICachedType> AlsoRegisterTypes => ImmutableArray<ICachedType>.Empty;

    public IReadOnlySet<ICachedType> ConcreteGeneralizations => ImmutableHashSet<ICachedType>.Empty;

    public ICachedType? DeclaringType => _declaringType ??= _typeCache.Get( _parameter.DeclaringType! );

    public int TypeDepth => 0;

    public ICachedType? GenericTypeDefinition => null;

    public ImmutableArray<ICachedType> GenericArguments => ImmutableArray<ICachedType>.Empty;

    public ImmutableArray<CustomAttributeData> AttributesData
    {
        get
        {
            if( _customAttributes.IsDefault )
            {
                _customAttributes = _parameter.CustomAttributes.ToImmutableArray();
            }
            return _customAttributes;
        }
    }

    public ImmutableArray<CachedMethod> DeclaredMethodInfos => ImmutableArray<CachedMethod>.Empty;

    public GlobalTypeCache TypeCache => _typeCache;

    public ImmutableArray<CachedMember> DeclaredMembers => ImmutableArray<CachedMember>.Empty;

    public ImmutableArray<CachedMember> Members => ImmutableArray<CachedMember>.Empty;

    public string Name => _parameter.Name;

    public ICachedType? ElementType => null;

    public EngineUnhandledType EngineUnhandledType => EngineUnhandledType.NullFullName;

    public ImmutableArray<object> RawAttributes
    {
        get
        {
            if( _attributes.IsDefault )
            {
                _attributes = ImmutableCollectionsMarshal.AsImmutableArray( _parameter.GetCustomAttributes( inherit: false ) );
            }
            return _attributes;
        }
    }

    public ImmutableArray<object> GetAttributes( IActivityMonitor monitor ) => ImmutableArray<object>.Empty;

    public bool TryGetAllAttributes( IActivityMonitor monitor, out ImmutableArray<object> attributes )
    {
        attributes = RawAttributes;
        return true;
    }

    public bool TryGetMapping( IActivityMonitor monitor, [NotNullWhen( true )] out CachedTypeMapping? mapping )
    {
        mapping = CachedTypeMapping.Empty;
        return true;
    }

    public StringBuilder Write( StringBuilder b ) => b.Append( _parameter.Name );

    public bool IsGenericType => false;

    public bool IsTypeDefinition => false;

    public bool IsSuperTypeDefiner => false;

    public bool IsTypeDefiner => false;

    // Should handle type constraints!
    public bool IsDelegate => false;

    // Should handle type constraints!
    public bool IsClassOrInterface => false;

    public override string ToString() => _parameter.Name;

}
