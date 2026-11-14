namespace CK.Engine.TypeCollector.Tests;

/// <summary>
/// Provides a common behavior to all the possible implementation of the <see cref="SomeEngineSpecAttribute"/>.
/// </summary>
public interface ISomeEngineSpecBehavior
{
    string DoSomethingWithTheSpecAttribute();
}
