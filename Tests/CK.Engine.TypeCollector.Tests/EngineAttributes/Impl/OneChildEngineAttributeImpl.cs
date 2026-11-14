namespace CK.Engine.TypeCollector.Tests;

public class OneChildEngineAttributeImpl : EngineAttributeImpl<ICachedType, OneChildEngineAttribute>,
                                           IAttributeHasNameProperty
{
    public string TheAttributeName => Attribute.Name;
}
