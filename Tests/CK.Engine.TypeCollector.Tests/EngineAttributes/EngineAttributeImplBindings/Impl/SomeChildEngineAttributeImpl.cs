namespace CK.Engine.TypeCollector.Tests;

public class SomeChildEngineAttributeImpl : EngineAttributeImpl<ICachedItem,SomeChildEngineAttribute>,
                                            IAttributeHasNameProperty
{
    public string TheAttributeName => Attribute.Name;
}
