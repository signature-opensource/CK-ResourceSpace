using CK.Core;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Abstraction for <see cref="EngineAttributeImpl{TItem,TAttr}"/>.
/// </summary>
/// <typeparam name="TItem">The type of the decorated item.</typeparam>
/// <typeparam name="TAttr">The attribute's type.</typeparam>
public interface IEngineAttributeImpl<out TItem, out TAttr> : IEngineAttributeImpl<TItem>
    where TItem : class, ICachedItem
    where TAttr : class, IEngineAttribute
{
    /// <inheritdoc cref="IEngineAttributeImpl.Attribute"/>
    new TAttr Attribute { get; }
}
