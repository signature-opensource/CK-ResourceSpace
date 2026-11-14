using CK.Core;
using System.Collections.Generic;

namespace CK.Engine.TypeCollector;


/// <summary>
/// Full strongly typed <see cref="EngineAttributeImpl"/> with a typed attribute, required <see cref="ChildEngineAttributeImpl{TItem, TAttr, TParent}.ParentImpl"/> 
/// and typed <see cref="ChildrenImpl"/>.
/// <para>
/// The <typeparamref name="TParent"/> and/or <typeparamref name="TChildren"/> can be <see cref="EngineAttributeImpl"/>.
/// </para>
/// </summary>
/// <typeparam name="TItem">The type of the decorated item.</typeparam>
/// <typeparam name="TAttr">The attribute type.</typeparam>
/// <typeparam name="TParent">The expected parent's type.</typeparam>
/// <typeparam name="TChildren">The children's type.</typeparam>
public class ChildEngineAttributeImpl<TItem,TAttr, TParent, TChildren> : ChildEngineAttributeImpl<TItem, TAttr, TParent>,
                                                                         IChildEngineAttributeImpl<TItem, TAttr, TParent, TChildren>
    where TItem : class, ICachedItem
    where TAttr : class, IEngineAttribute
    where TParent : class, IEngineAttributeImpl
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
