using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CK.Setup;

/// <summary>
/// A property that its code can change while the application runs (<see cref="ConfigurationLevel.Run"/>): a value of
/// the section of an App singleton (<see cref="ConfigurationSectionAttribute"/>). The App builder declares them (a
/// <c>"$LOCK"</c> can restrict them), the engine of the dynamic configuration generates their accessors.
/// </summary>
public sealed class ConfigurationRunProperty
{
    ConfigurationRunProperty( Type owner, string section, IReadOnlyList<PropertyInfo> chain )
    {
        Owner = owner;
        Chain = chain;
        Path = $"{section}:{string.Join( ':', chain.Select( p => p.Name ) )}";
    }

    /// <summary>
    /// Gets the App singleton that owns the section.
    /// </summary>
    public Type Owner { get; }

    /// <summary>
    /// Gets the properties from the owner to the value (the last one).
    /// </summary>
    public IReadOnlyList<PropertyInfo> Chain { get; }

    /// <summary>
    /// Gets the path of the value: the section, then the names of the <see cref="Chain"/> (':' separated).
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the type of the value.
    /// </summary>
    public Type ValueType => Chain[^1].PropertyType;

    /// <summary>
    /// Gets whether a type is a basic value type of a run property: a number, a boolean, a string, an enum, a
    /// <see cref="TimeSpan"/> or a <see cref="Guid"/>.
    /// </summary>
    /// <param name="t">The type.</param>
    /// <returns>True for a basic type.</returns>
    public static bool IsBasicType( Type t ) => t.IsEnum
                                                || t == typeof( string ) || t == typeof( bool )
                                                || t == typeof( int ) || t == typeof( long ) || t == typeof( short ) || t == typeof( byte )
                                                || t == typeof( uint ) || t == typeof( ulong ) || t == typeof( ushort ) || t == typeof( sbyte )
                                                || t == typeof( double ) || t == typeof( float ) || t == typeof( decimal )
                                                || t == typeof( TimeSpan ) || t == typeof( Guid );

    /// <summary>
    /// Gets the section declared by a type (<see cref="ConfigurationSectionAttribute"/>), or null.
    /// </summary>
    /// <param name="t">The type.</param>
    /// <returns>The section name or null.</returns>
    public static string? GetSection( Type t ) => t.GetCustomAttribute<ConfigurationSectionAttribute>( inherit: false )?.Name;

    /// <summary>
    /// Collects the run properties of a type that declares a section: its properties decorated with
    /// <see cref="ConfigurationLevelAttribute"/> (<see cref="ConfigurationLevel.Run"/>) and, through a decorated property
    /// whose type is a class, the ones of this class.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="owner">The type that declares the section.</param>
    /// <param name="properties">The run properties.</param>
    /// <returns>True on success, false on error (errors are logged).</returns>
    public static bool TryCollect( IActivityMonitor monitor, Type owner, out List<ConfigurationRunProperty> properties )
    {
        properties = new List<ConfigurationRunProperty>();
        var section = GetSection( owner );
        if( section == null ) return true;
        if( string.IsNullOrWhiteSpace( section ) || section.Contains( ':' ) )
        {
            monitor.Error( $"Invalid [ConfigurationSection( \"{section}\" )] on '{owner.ToCSharpName()}': a section is a root key of the App." );
            return false;
        }
        return Collect( monitor, owner, section, owner, [], properties );

        static bool Collect( IActivityMonitor monitor, Type owner, string section, Type t, List<PropertyInfo> chain, List<ConfigurationRunProperty> properties )
        {
            bool success = true;
            foreach( var p in t.GetProperties( BindingFlags.Public | BindingFlags.Instance ) )
            {
                var a = p.GetCustomAttribute<ConfigurationLevelAttribute>( inherit: true );
                if( a == null ) continue;
                var where = $"'{owner.ToCSharpName()}.{string.Join( '.', chain.Select( c => c.Name ).Append( p.Name ) )}'";
                if( a.Level != ConfigurationLevel.Run )
                {
                    monitor.Error( $"[ConfigurationLevel( {a.Level} )] on {where}: only Run is declared (Init is the capability of what the code reads at start)." );
                    success = false;
                    continue;
                }
                var path = chain.Append( p ).ToList();
                if( IsBasicType( p.PropertyType ) )
                {
                    if( p.GetMethod?.IsPublic != true || p.SetMethod?.IsPublic != true || p.GetIndexParameters().Length > 0 )
                    {
                        monitor.Error( $"The run property {where} must have a public getter and a public setter." );
                        success = false;
                        continue;
                    }
                    properties.Add( new ConfigurationRunProperty( owner, section, path ) );
                }
                else if( p.PropertyType.IsClass && p.GetMethod?.IsPublic == true && chain.Count < 8 && !chain.Any( c => c.PropertyType == p.PropertyType ) )
                {
                    success &= Collect( monitor, owner, section, p.PropertyType, path, properties );
                }
                else
                {
                    monitor.Error( $"The run property {where} must be a basic value (a number, a boolean, a string, an enum, a TimeSpan or a Guid) or a class with run properties (reached through a public getter)." );
                    success = false;
                }
            }
            return success;
        }
    }

    /// <inheritdoc />
    public override string ToString() => Path;
}
