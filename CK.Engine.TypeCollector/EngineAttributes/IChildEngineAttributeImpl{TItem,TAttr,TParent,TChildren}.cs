using CK.Core;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Abstraction for <see cref="ChildEngineAttributeImpl{TItem, TAttr, TParent, TChildren}"/>.
/// </summary>
/// <typeparam name="TItem">The type of the decorated item.</typeparam>
/// <typeparam name="TAttr">The attribute type.</typeparam>
/// <typeparam name="TParent">The expected parent's type.</typeparam>
/// <typeparam name="TChildren">The children's type.</typeparam>
public interface IChildEngineAttributeImpl<out TItem, out TAttr, out TParent, out TChildren> : IEngineAttributeImpl<TItem, TAttr, TChildren>,
                                                                                               IChildEngineAttributeImpl<TItem, TAttr, TParent>
    where TItem : class, ICachedItem
    where TAttr : class, IEngineAttribute
    where TParent : class, IEngineAttributeImpl
    where TChildren : class, IEngineAttributeImpl
{
}
