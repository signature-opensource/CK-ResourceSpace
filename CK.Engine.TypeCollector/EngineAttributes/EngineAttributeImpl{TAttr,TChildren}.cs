using CK.Core;
using System.Collections.Generic;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Base engine implementation with strongly typed <see cref="EngineAttributeImpl{TItem, TAttr}.Attribute"/> and
/// <see cref="ChildrenImpl"/>.
/// This base class puts no constraints on <see cref="EngineAttributeImpl.ParentImpl"/> that is typically null
/// if this is bound to a <see cref="IEngineAttribute"/> and not a <see cref="IChildEngineAttribute{T}"/> but can be used
/// for child implementation if types parent is useless.
/// </summary>
/// <typeparam name="TItem">The type of the decorated item.</typeparam>
/// <typeparam name="TAttr">The attribute's type.</typeparam>
/// <typeparam name="TChildren">The children's type.</typeparam>
public abstract class EngineAttributeImpl<TItem, TAttr, TChildren> : EngineAttributeImpl<TItem, TAttr>,
                                                                     IEngineAttributeImpl<TItem, TAttr, TChildren>
    where TItem : class, ICachedItem
    where TAttr : class, IEngineAttribute
    where TChildren : class, IEngineAttributeImpl
{
    IReadOnlyCollection<TChildren>? _children;

    internal override sealed bool OnAddChild( IActivityMonitor monitor, EngineAttributeImpl c )
    {
        return CheckChildType( monitor, this, typeof( TChildren ), c.GetType() );
    }

    /// <inheritdoc />
    public new IReadOnlyCollection<TChildren> ChildrenImpl => _children ??= CreateTypedChildrenAdapter<TChildren>( this );

}
