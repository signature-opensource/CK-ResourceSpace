using CK.Core;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Abstraction for <see cref="ChildEngineAttributeImpl{TItem,TAttr,TParent}"/>.
/// </summary>
/// <typeparam name="TItem">The type of the decorated item.</typeparam>
/// <typeparam name="TAttr">The attribute type.</typeparam>
/// <typeparam name="TParent">The expected parent's type.</typeparam>
public interface IChildEngineAttributeImpl<out TItem,out TAttr,out TParent> : IEngineAttributeImpl<TItem,TAttr>
    where TItem : class, ICachedItem
    where TAttr : class, IEngineAttribute
    where TParent : class, IEngineAttributeImpl
{
    /// <summary>
    /// Gets the parent implementation.
    /// </summary>
    new TParent ParentImpl { get; }
}
