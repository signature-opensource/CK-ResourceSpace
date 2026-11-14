using CK.Core;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Cached <see cref="EventInfo"/>.
/// </summary>
public sealed class CachedEvent : CachedMember
{
    ICachedType? _handlerType;

    internal CachedEvent( ICachedType declaringType, EventInfo ev )
        : base( declaringType, ev )
    {
    }

    /// <summary>
    /// Gets the handler type (the delegate type).
    /// </summary>
    public ICachedType EventHandlerType
    {
        get
        {
            if( _handlerType == null )
            {
                var h = EventInfo.EventHandlerType;
                if( h == null )
                {
                    // Highly pathological case: see https://stackoverflow.com/questions/78029989/why-does-eventinfo-eventhandlertype-return-a-nullable-type-value
                    // We don't want to impact the API with this:
                    // - We can throw.
                    // - We can use a placeholder type. The naked Action seems appropriate.
                    ActivityMonitor.StaticLogger.Warn( ActivityMonitor.Tags.ToBeInvestigated, $"""
                                                       Event '{DeclaringType}.{Name}' has a null event handler type.
                                                       That is not possible in C# but may be achieved at the IL level. Event handler type is considered to be Action.
                                                       """ );
                    h = typeof( Action );
                }
                _handlerType = TypeCache.Get( h );
            }
            return _handlerType;
        }
    }

    /// <summary>
    /// Gets the <see cref="EventInfo"/>.
    /// </summary>
    public EventInfo EventInfo => Unsafe.As<EventInfo>( _member );

    internal override StringBuilder Write( StringBuilder b, bool withDeclaringType )
    {
        EventHandlerType.Write(  b );
        b.Append( ' ' );
        if( withDeclaringType ) b.Append( DeclaringType.CSharpName ).Append( '.' );
        b.Append( Name );
        return b;
    }
}
