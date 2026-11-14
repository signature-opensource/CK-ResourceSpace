namespace CK.Engine.TypeCollector;

/// <summary>
/// Abstraction for <see cref="EngineAttributeImpl{TImpl}"/>.
/// </summary>
/// <typeparam name="TItem">The type of the decorated item.</typeparam>
public interface IEngineAttributeImpl<out TItem> : IEngineAttributeImpl
    where TItem : class, ICachedItem
{
    /// <summary>
    /// Gets the strongly typed decorated item.
    /// </summary>
    new TItem DecoratedItem { get; }
}
