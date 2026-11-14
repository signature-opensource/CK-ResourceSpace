namespace CK.Engine.TypeCollector.Tests;

public class OneEngineAttributeImpl : EngineAttributeImpl<ICachedType,OneEngineAttribute,OneChildEngineAttributeImpl>,
                                      IAttributeHasNameProperty
{
    public string TheAttributeName => Attribute.Name;
}

