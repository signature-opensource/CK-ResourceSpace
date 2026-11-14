namespace CK.Engine.TypeCollector.Tests;

/// <summary>
/// Independent implementation for attribute <see cref="SomeEngineSpecAttribute"/>.
/// </summary>
public class SomeEngineSpecAttributeDifferentImpl : EngineAttributeImpl<ICachedItem, SomeEngineSpecAttribute>,
                                                    ISomeEngineSpecBehavior
{
    public string DoSomethingWithTheSpecAttribute()
    {
        return $"Totally independent [Spec] implementation can do anything with {Attribute.SomethingMore}.";
    }
}
