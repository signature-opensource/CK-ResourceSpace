using CK.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace CK.Engine.TypeCollector;

sealed partial class CachedType : CachedItem, ICachedType
{
    readonly GlobalTypeCache _cache;
    readonly CachedAssembly _assembly;
    readonly ImmutableArray<ICachedType> _interfaces;
    readonly ICachedType? _baseType;
    [AllowNull]readonly ICachedType _nullable;

    ImmutableArray<ICachedType> _directInterfaces;
    ImmutableArray<ICachedType> _alsoRegisterTypes;
    IReadOnlySet<ICachedType>? _concreteGeneralizations;
    ICachedType? _declaringType;
    ICachedType? _elementType;
    ImmutableArray<CachedMember> _members;
    ImmutableArray<CachedMember> _declaredMembers;
    ICachedType? _genericTypeDefinition;
    ImmutableArray<ICachedType> _genericArguments;
    string? _csharpName;
    readonly ushort _typeDepth;

    const EngineUnhandledType _uninitialized = (EngineUnhandledType)0xFF;
    EngineUnhandledType _unhandledType;

    // These should be Flags.
    // 32 flags may be enough but they should not be exposed. The following properties
    // and combination of properties are often needed:
    //
    // IsClass, IsInterface, IsValueType, IsEnum, IsStruct, IsGenericType, IsGenericTypeDefinition, IsClassOrInterface,
    // IsTypeDefiner, IsSuperTypeDefiner, IsCKMultiple, IsCKSingle
    // IsPoco, IsAbstractPoco, IsVirtualPoco, IsPrimaryPoco, IsSecondaryPoco,
    // IsAutoService, IsScopedAutoService, IsSingletonAutoService, IsContainerConfiguredScopedService, IsContainerConfiguredSingletonService,
    // IsRealObject.
    //
    // This requires more thoughts.
    // In a first time, we naïvely implement these properties when we actually need them with bool or bool?.
    // A bool CheckValid( IActivityMonitor monitor ) should be implemented OR we may use the static logger
    // to detect incoherencies.
    // 
    readonly bool _isGenericType;
    readonly bool _isGenericTypeDefinition;
    // Initialized by the cache for Delegate base class
    // to avoid deferred resolution.
    internal bool _isDelegate;
    bool? _isSuperTypeDefiner;
    bool? _isTypeDefiner;

    sealed class NullValueType : ICachedType
    {
        readonly CachedType _nonNullable;
        readonly Type _type;
        string? _name;

        public NullValueType( CachedType nonNullable, Type t )
        {
            Throw.DebugAssert( System.Nullable.GetUnderlyingType( t ) == nonNullable.Type );
            _nonNullable = nonNullable;
            _type = t;
        }

        public Type Type => _type;

        public bool IsTypeDefinition => _nonNullable.IsTypeDefinition;

        public bool IsGenericType => _nonNullable.IsGenericType;

        public bool IsSuperTypeDefiner => false;

        public bool IsTypeDefiner => false;

        public bool IsDelegate => false;

        public bool IsClassOrInterface => false;

        public int TypeDepth => _nonNullable.TypeDepth;

        public CachedAssembly Assembly => _nonNullable.Assembly;

        public bool? IsNullable => true;

        public ICachedType Nullable => this;

        public ICachedType NonNullable => _nonNullable;

        public string CSharpName => _name ??= _nonNullable.CSharpName + "?";

        public string Name => _nonNullable.Name;

        public ImmutableArray<ICachedType> Interfaces => _nonNullable.Interfaces;

        public ImmutableArray<ICachedType> DirectInterfaces => _nonNullable.DirectInterfaces;

        public ICachedType? BaseType => null;

        public ImmutableArray<ICachedType> AlsoRegisterTypes => _nonNullable.AlsoRegisterTypes;

        public IReadOnlySet<ICachedType> ConcreteGeneralizations => _nonNullable.ConcreteGeneralizations;

        public ICachedType? DeclaringType => _nonNullable.DeclaringType;

        public ICachedType? GenericTypeDefinition => _nonNullable.GenericTypeDefinition;

        public ImmutableArray<ICachedType> GenericArguments => _nonNullable.GenericArguments;

        public ImmutableArray<CustomAttributeData> AttributesData => _nonNullable.AttributesData;

        public ImmutableArray<CachedMember> DeclaredMembers => _nonNullable.DeclaredMembers;

        public ImmutableArray<CachedMember> Members => _nonNullable.Members;

        public GlobalTypeCache TypeCache => _nonNullable.TypeCache;

        public ICachedType? ElementType => _nonNullable.ElementType;

        public EngineUnhandledType EngineUnhandledType => _nonNullable.EngineUnhandledType;

        public ImmutableArray<object> RawAttributes => _nonNullable.RawAttributes;

        public StringBuilder Write( StringBuilder b ) => b.Append( CSharpName );

        public override string ToString() => CSharpName;

        public bool TryGetAllAttributes( IActivityMonitor monitor, out ImmutableArray<object> attributes )
            => _nonNullable.TryGetAllAttributes( monitor, out attributes );

        public bool TryGetMapping( IActivityMonitor monitor, [NotNullWhen( true )] out CachedTypeMapping? mapping )
            => _nonNullable.TryGetMapping( monitor, out mapping );
    }

    CachedType( GlobalTypeCache cache,
                Type type,
                int typeDepth,
                CachedAssembly assembly,
                ImmutableArray<ICachedType> interfaces )
        : base( type )
    {
        _cache = cache;
        _typeDepth = (ushort)typeDepth;
        _assembly = assembly;
        _interfaces = interfaces;
        if( interfaces.IsEmpty ) _directInterfaces = interfaces;
        _isGenericTypeDefinition = type.IsGenericTypeDefinition;
        _isGenericType = type.IsGenericType;
        if( !_isGenericType ) _genericArguments = ImmutableArray<ICachedType>.Empty;
    }

    // Reference type.
    internal CachedType( GlobalTypeCache cache,
                         Type type,
                         int typeDepth,
                         CachedAssembly assembly,
                         ImmutableArray<ICachedType> interfaces,
                         ICachedType? baseType )
        : this( cache, type, typeDepth, assembly, interfaces )
    {
        _baseType = baseType;
        _nullable = this;
        _isDelegate = baseType != null && baseType.IsDelegate;
    }

    // Value type.
    internal CachedType( GlobalTypeCache cache,
                         Type type,
                         int typeDepth,
                         Type? nullableValueType,
                         CachedAssembly assembly,
                         ImmutableArray<ICachedType> interfaces )
        : this( cache, type, typeDepth, assembly, interfaces )
    {
        _nullable = nullableValueType != null
                    ? new NullValueType( this, nullableValueType )
                    : this;
        _isSuperTypeDefiner = false;
        _isTypeDefiner = false;
    }

    public override sealed GlobalTypeCache TypeCache => _cache;

    public Type Type => Unsafe.As<Type>( _member );

    public int TypeDepth => _typeDepth;

    public CachedAssembly Assembly => _assembly;

    public ImmutableArray<ICachedType> Interfaces => _interfaces;

    public ImmutableArray<ICachedType> DirectInterfaces => _directInterfaces.IsDefault ? ComputeDirectInterfaces() : _directInterfaces;

    ImmutableArray<ICachedType> ComputeDirectInterfaces()
    {
        Throw.DebugAssert( !_interfaces.IsEmpty );
        var b = ImmutableArray.CreateBuilder<ICachedType>( _interfaces.Length );
        foreach( var i in _interfaces )
        {
            if( _baseType != null && _baseType.Interfaces.Contains( i ) ) continue;
            if( AppearAbove( i, _interfaces ) ) continue;
            b.Add( i );
        }
        _directInterfaces = b.DrainToImmutable();
        return _directInterfaces;

        static bool AppearAbove( ICachedType candidate, ImmutableArray<ICachedType> interfaces  )
        {
            foreach( var i in interfaces )
            {
                if( candidate != i && i.Interfaces.Contains( candidate ) )
                {
                    return true;
                }
            }
            return false;
        }
    }

    public ICachedType? BaseType => _baseType;

    public ImmutableArray<ICachedType> AlsoRegisterTypes => _alsoRegisterTypes.IsDefault ? ComputeAlsoRegisterTypes() : _alsoRegisterTypes;

    ImmutableArray<ICachedType> ComputeAlsoRegisterTypes()
    {
        ImmutableArray<ICachedType>.Builder? b = null;
        foreach( var a in AttributesData )
        {
            var t = a.AttributeType;
            if( t.Namespace == "CK.Core" )
            {
                Throw.DebugAssert( "AlsoRegisterTypeAttribute`".Length == 26 ); 
                var sName = t.Name.AsSpan();
                if( sName.StartsWith( "AlsoRegisterTypeAttribute`", StringComparison.Ordinal )
                    && int.TryParse( sName.Slice( 26 ), System.Globalization.CultureInfo.InvariantCulture, out int count )
                    && count is > 0 and < 9 )
                {
                    b ??= ImmutableArray.CreateBuilder<ICachedType>();
                    foreach( var also in t.GetGenericArguments() )
                    {
                        b.Add( _cache.Get( also ) );
                    }
                }
            }
        }
        _alsoRegisterTypes = b != null ? b.DrainToImmutable() : ImmutableArray<ICachedType>.Empty;
        return _alsoRegisterTypes;
    }

    public IReadOnlySet<ICachedType> ConcreteGeneralizations => _concreteGeneralizations ??= ComputeConcreteGeneralizations();

    IReadOnlySet<ICachedType> ComputeConcreteGeneralizations()
    {
        HashSet<ICachedType> set = new HashSet<ICachedType>();
        foreach( var i in Interfaces )
        {
            if( !i.IsTypeDefiner )
            {
                set.Add( i );
            }
        }
        var b = _baseType;
        while( b != null )
        {
            if( !b.IsTypeDefiner ) set.Add( b );
            b = b.BaseType;
        }
        return set.Count > 0 ? set : ImmutableHashSet<ICachedType>.Empty;
    }

    public ICachedType? DeclaringType
    {
        get
        {
            if( _declaringType == null )
            {
                var t = Type.DeclaringType;
                _declaringType = t != null ? _cache.Get( t ) : _nullMarker;
            }
            return NullMarker.Filter( _declaringType );
        }
    }

    public EngineUnhandledType EngineUnhandledType
    {
        get
        {
            if( _unhandledType == _uninitialized )
            {
                if( Type.FullName == null ) _unhandledType = EngineUnhandledType.NullFullName;
                else if( _assembly.Assembly.IsDynamic ) _unhandledType = EngineUnhandledType.FromDynamicAssembly;
                else if( !Type.IsVisible ) _unhandledType = EngineUnhandledType.NotVisible;
                else if( !Type.IsValueType || !Type.IsClass || !Type.IsInterface || !Type.IsEnum ) _unhandledType = EngineUnhandledType.NotClassEnumValueTypeOrEnum;
                else _unhandledType = EngineUnhandledType.None;
                   
            }
            return _unhandledType;
        }
    }

    public ICachedType? GenericTypeDefinition => _genericTypeDefinition ??= _isGenericType ? _cache.Get( Type.GetGenericTypeDefinition() ) : null;

    public string CSharpName => _csharpName ??= Type.ToCSharpName();

    public bool? IsNullable => _nullable == this ? null : false;

    public ICachedType Nullable => _nullable;

    public ICachedType NonNullable => this;

    public ICachedType? ElementType => NullMarker.Filter( _elementType ??= Type.HasElementType ? _cache.Get( Type.GetElementType()! ) : _nullMarker );

    public ImmutableArray<ICachedType> GenericArguments
    {
        get
        {
            if( _genericArguments.IsDefault )
            {
                Throw.DebugAssert( _isGenericType );
                var arguments = Type.GetGenericArguments();
                var b = ImmutableArray.CreateBuilder<ICachedType>( arguments.Length );
                foreach( Type a in arguments )
                {
                    b.Add( _cache.Get( a ) );
                }
                _genericArguments = b.MoveToImmutable();
            }
            return _genericArguments;
        }
    }

    public ImmutableArray<CachedMember> DeclaredMembers
    {
        get
        {
            if( _declaredMembers.IsDefault ) ComputeMembers();
            return _declaredMembers;
        }
    }

    public ImmutableArray<CachedMember> Members
    {
        get
        {
            if( _members.IsDefault ) ComputeMembers();
            return _members;
        }
    }

    void ComputeMembers()
    {
        Throw.DebugAssert( _declaredMembers.IsDefault );
        var members = Type.GetMembers( BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static );
        var bD = ImmutableArray.CreateBuilder<CachedMember>( members.Length );
        var bM = ImmutableArray.CreateBuilder<CachedMember>( members.Length );
        foreach( var m in members )
        {
            var map = m switch
            {
                MethodInfo method => new CachedMethod( this, method ),
                ConstructorInfo ctor => new CachedConstructor( this, ctor ),
                PropertyInfo prop => new CachedProperty( this, prop ),
                EventInfo ev => new CachedEvent( this, ev ),
                FieldInfo f => new CachedField( this, f ),
                Type nested => null,
                _ => Throw.NotSupportedException<CachedMember>( m.ToString() )
            };
            if( map != null )
            {
                if( m.DeclaringType == _member )
                {
                    bD.Add( map );
                }
                bM.Add( map );
            }
        }
        _declaredMembers = bD.DrainToImmutable();
        _members = bM.DrainToImmutable();
    }

    public override StringBuilder Write( StringBuilder b ) => b.Append( CSharpName );

    public override string ToString() => CSharpName;
}
