namespace CK.TypeCache.Tests;

public class OneChildEngineAttributeImpl : EngineAttributeImpl<ICachedType, OneChildEngineAttribute>,
                                           IAttributeHasNameProperty
{
    public string TheAttributeName => Attribute.Name;
}
