namespace CK.Engine.TypeCollector.Tests;

/// <summary>
/// Specialized SomeEngineAttributeImpl with a strongly typed Attribute thanks to
/// the <see cref="SomeEngineAttributeImpl{TAttr}"/> helper.
/// <para>
/// See <see cref="SomeEngineSpecAttribute"/>.
/// </para>
/// </summary>
public class SomeEngineSpecAttributeImpl : SomeEngineAttributeImpl<SomeEngineSpecAttribute>
{
    public override string DoSomethingWithTheSpecAttribute()
    {
        return $"I'm the [Spec] implementation, I can do stuff with {Attribute.SomethingMore}.";
    }
}
