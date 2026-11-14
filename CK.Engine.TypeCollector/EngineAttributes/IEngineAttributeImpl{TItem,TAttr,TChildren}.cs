using CK.Core;
using System.Collections.Generic;

namespace CK.Engine.TypeCollector;


/// <summary>
/// Abstraction for <see cref="EngineAttributeImpl{TItem, TAttr, TChildren}"/>.
/// </summary>
/// <typeparam name="TItem">The type of the decorated item.</typeparam>
/// <typeparam name="TAttr">The attribute's type.</typeparam>
/// <typeparam name="TChildren">The children's type.</typeparam>
public interface IEngineAttributeImpl<out TItem, out TAttr, out TChildren> : IEngineAttributeImpl<TItem, TAttr>
    where TItem : class, ICachedItem
    where TAttr : class, IEngineAttribute
    where TChildren : class, IEngineAttributeImpl
{
    /// <summary>
    /// Gets the typed children's attribute.
    /// </summary>
    new IReadOnlyCollection<TChildren> ChildrenImpl { get; }
}
