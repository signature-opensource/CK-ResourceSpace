using CK.Core;
using System.Linq;

namespace CK.TypeCache;

sealed partial class CachedType
{
    bool? _isOptional;
    // Computed once. A failure is memorized: the errors are logged only once.
    IConfigurableTypeAttribute? _configurable;
    bool _configurableComputed;
    bool _configurableFailed;

    public bool IsOptional => _isOptional ??= ComputeIsOptional();

    bool ComputeIsOptional()
    {
        // The attribute data first: the attributes are instantiated only on the type that declares one.
        if( AttributesData.Any( a => typeof( IOptionalResourceGroupAttribute ).IsAssignableFrom( a.AttributeType ) ) )
        {
            return RawAttributes.OfType<IOptionalResourceGroupAttribute>().All( a => a.IsOptional );
        }
        return _baseType != null && _baseType.IsOptional;
    }

    public bool TryGetConfigurableType( IActivityMonitor monitor, out IConfigurableTypeAttribute? configurable )
    {
        if( !_configurableComputed )
        {
            _configurableComputed = true;
            _configurableFailed = !ComputeConfigurable( monitor );
        }
        configurable = _configurable;
        return !_configurableFailed;
    }

    bool ComputeConfigurable( IActivityMonitor monitor )
    {
        // The attribute data first: most types stop here.
        if( !AttributesData.Any( a => typeof( IConfigurableTypeAttribute ).IsAssignableFrom( a.AttributeType ) ) )
        {
            return true;
        }
        var declared = RawAttributes.OfType<IConfigurableTypeAttribute>().ToList();
        var attr = declared[0];
        var attrName = attr.GetType().ToCSharpName();
        bool success = true;
        if( declared.Count > 1 )
        {
            monitor.Error( $"Type '{CSharpName}' has {declared.Count} configurable type attributes ({declared.Select( a => $"[{a.GetType().ToCSharpName()}]" ).Concatenate()}): a type has at most one." );
            success = false;
        }
        var t = Type;
        if( !t.IsClass || t.IsAbstract || _isDelegate || !typeof( IResourceGroup ).IsAssignableFrom( t ) )
        {
            monitor.Error( $"Type '{CSharpName}' is decorated with [{attrName}] but it is not a package (a concrete IResourceGroup): only a package is a configurable type." );
            success = false;
        }
        if( !ICachedType.IsValidConfigurationName( attr.Name ) )
        {
            monitor.Error( $"The configuration name '{attr.Name}' of '{CSharpName}' ([{attrName}]) is not valid: letters, digits and dots, starting with a letter." );
            success = false;
        }
        if( attr.Activation is not (TypeActivation.Optional or TypeActivation.ByDefault or TypeActivation.Required) )
        {
            monitor.Error( $"The activation of '{CSharpName}' ([{attrName}]) must be Optional, ByDefault or Required (it is '{attr.Activation}')." );
            success = false;
        }
        if( success ) _configurable = attr;
        return success;
    }
}
