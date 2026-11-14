using CK.Core;
using System;
using System.Linq;

namespace CK.Engine.TypeCollector;

sealed partial class CachedType
{
    public bool IsTypeDefinition => _isGenericTypeDefinition;

    public bool IsGenericType => _isGenericType;

    public bool IsInterface => Type.IsInterface;

    public bool IsSuperTypeDefiner => _isSuperTypeDefiner ??= AttributesData.Any( a => a.AttributeType == typeof( CKTypeSuperDefinerAttribute ) );

    public bool IsTypeDefiner => _isTypeDefiner ??= ComputeTypeDefiner();

    public bool IsDelegate => _isDelegate;

    public bool IsClassOrInterface => (!_isDelegate && Type.IsClass) || Type.IsInterface;

    bool ComputeTypeDefiner()
    {
        return IsSuperTypeDefiner
               || AttributesData.Any( a => a.AttributeType == typeof( CKTypeDefinerAttribute ) )
               || (_baseType != null && _baseType.IsSuperTypeDefiner)
               || DirectInterfaces.Any( i => i.IsSuperTypeDefiner ); 
    }
}
