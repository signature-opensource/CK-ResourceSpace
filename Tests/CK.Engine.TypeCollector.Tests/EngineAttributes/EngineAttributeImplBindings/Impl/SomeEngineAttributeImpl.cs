namespace CK.Engine.TypeCollector.Tests;

/// <summary>
/// Specializable. The <see cref="SomeEngineAttributeImpl{TAttr}"/> provides a strongly typed Attribute.
/// </summary>
public class SomeEngineAttributeImpl : EngineAttributeImpl<ICachedItem, SomeEngineAttribute>,
                                       IAttributeHasNameProperty,
                                       ISomeEngineSpecBehavior
{
    public string TheAttributeName => Attribute.Name;

    /// <summary>
    /// This is virtual: a specialization can alter this behavior, <see cref="SomeEngineSpecAttributeImpl"/>
    /// does that.
    /// </summary>
    /// <returns></returns>
    public virtual string DoSomethingWithTheSpecAttribute()
    {
        if( Attribute is SomeEngineSpecAttribute spec )
        {
            return $"My Attribute is a Spec and it has {spec.SomethingMore}.";
        }
        return $"My Attribute is a NOT a Spec. I give up.";
    }
}
