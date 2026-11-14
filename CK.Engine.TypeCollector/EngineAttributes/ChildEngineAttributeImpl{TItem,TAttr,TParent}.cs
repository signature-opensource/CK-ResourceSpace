using CK.Core;
using System.Runtime.CompilerServices;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Full strongly typed <see cref="EngineAttributeImpl"/> with a typed attribute and required <see cref="ParentImpl"/>.
/// </summary>
/// <typeparam name="TItem">The type of the decorated item.</typeparam>
/// <typeparam name="TAttr">The attribute type.</typeparam>
/// <typeparam name="TParent">The expected parent's type.</typeparam>
public class ChildEngineAttributeImpl<TItem, TAttr, TParent> : EngineAttributeImpl<TItem,TAttr>,
                                                               IChildEngineAttributeImpl<TItem, TAttr, TParent>
    where TItem : class, ICachedItem
    where TAttr : class, IEngineAttribute
    where TParent : class, IEngineAttributeImpl
{
    internal override bool SetFields( IActivityMonitor monitor,
                                      ICachedItem item,
                                      IEngineAttribute attribute,
                                      EngineAttributeImpl? parentImpl )
    {
        // Use logical and (non short-circuit).
        return base.SetFields( monitor, item, attribute, parentImpl )
               & CheckParentType( monitor, this, typeof( TParent ), parentImpl );
    }

    /// <summary>
    /// Gets the typed parent attribute implementation.
    /// </summary>
    public new TParent ParentImpl => Unsafe.As<TParent>( base.ParentImpl! );

}
