using CK.Core;

namespace CK.TypeCache.Tests;

public sealed class CanBeBuggyAttributeImpl : EngineAttributeImpl<ICachedItem, CanBeBuggyAttribute>, IAttributeHasNameProperty
{
    public string TheAttributeName => Attribute.Name;

    protected override bool OnInitialized( IActivityMonitor monitor )
    {
        if( CanBeBuggyAttribute.ImplOnInitializedThrow )
        {
            Throw.CKException( "Expected: CanBeBuggyAttribute.ImplOnInitializedThrow is true." ); 
        }
        return !CanBeBuggyAttribute.ImplOnInitializedFalse;
    }
}
