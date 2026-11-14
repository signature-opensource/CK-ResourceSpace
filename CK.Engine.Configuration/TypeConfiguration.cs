using CK.Core;
using System;
using System.Xml.Linq;

namespace CK.Setup;

/// <summary>
/// Models the &lt;Type&gt; elements that are children of <see cref="TypeConfigurationSet"/> &lt;Types&gt;.
/// </summary>
/// <param name="Type">The type</param>
/// <param name="Kind">The type kind. <see cref="ConfigurableAutoServiceKind.None"/> only ensures that the type is registered.</param>
public sealed record TypeConfiguration( Type Type, ConfigurableAutoServiceKind Kind = ConfigurableAutoServiceKind.None )
{
    internal TypeConfiguration( XElement e )
        : this( ReadType( e ), ReadKind( e ) )
    {
        if( e.Attribute( EngineConfiguration.xOptional ) != null )
        {
            Throw.InvalidDataException( "Obsolete Optional attribute. Please remove it." );
        }
        Type = SimpleTypeFinder.WeakResolver( (string?)e.Attribute( EngineConfiguration.xName ) ?? e.Value, throwOnError: true )!;
    }

    static Type ReadType( XElement e )
    {
        return SimpleTypeFinder.WeakResolver( (string?)e.Attribute( EngineConfiguration.xName ) ?? e.Value, throwOnError: true )!;
    }

    static ConfigurableAutoServiceKind ReadKind( XElement e )
    {
        var k = (string?)e.Attribute( EngineConfiguration.xKind );
        return k != null ? (ConfigurableAutoServiceKind)Enum.Parse( typeof( ConfigurableAutoServiceKind ), k.Replace( '|', ',' ) ) : ConfigurableAutoServiceKind.None;
    }
}
