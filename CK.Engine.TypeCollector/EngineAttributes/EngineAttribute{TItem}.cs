using CK.Core;
using System;
using System.Runtime.CompilerServices;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Base class for all <see cref="EngineAttribute"/> implementations that expect the <see cref="DecoratedItem"/>
/// to be a <typeparamref name="TItem"/> (may be the base <see cref="ICachedItem"/> if this implementation can work
/// with any kind of decorated item).
/// </summary>
/// <typeparam name="TItem">The type of the decorated item.</typeparam>
public abstract class EngineAttributeImpl<TItem> : EngineAttributeImpl, IEngineAttributeImpl<TItem>
    where TItem : class, ICachedItem
{
    internal override bool SetFields( IActivityMonitor monitor,
                                      ICachedItem item,
                                      IEngineAttribute attribute,
                                      EngineAttributeImpl? parentImpl )
    {
        return base.SetFields( monitor, item, attribute, parentImpl )
               & CheckDecoratedItem( monitor, item );
    }

    bool CheckDecoratedItem( IActivityMonitor monitor, ICachedItem item )
    {
        Throw.DebugAssert( typeof(TItem).Name.StartsWith( "ICached" ) );
        if( item is not TItem )
        {
            monitor.Error( $"""
                Attribute implementation '{GetType():N}' for [{AttributeName}] on '{item}' item type mismatch.
                This implementation expects the attribute to decorate a {typeof( TItem ).Name.AsSpan( 7 )}.
                """ );
            return false;
        }
        return true;
    }

    /// <inheritdoc />
    public new TItem DecoratedItem => Unsafe.As<TItem>( _decoratedItem );

}
