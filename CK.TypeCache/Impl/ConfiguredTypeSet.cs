using CK.Core;
using CK.Setup;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CK.TypeCache;

/// <summary>
/// The mutable working set of the engine while it selects the types of a BinPath: public for the engine's projects
/// (CK.TypeSet), not a contract to keep.
/// </summary>
public sealed class ConfiguredTypeSet : IConfiguredTypeSet
{
    readonly HashSet<ICachedType> _allTypes;
    readonly TypeConfigurationSet _configuredTypes;

    /// <summary>
    /// Initializes an empty set.
    /// </summary>
    public ConfiguredTypeSet()
    {
        _allTypes = new HashSet<ICachedType>();
        _configuredTypes = new TypeConfigurationSet();
    }

    /// <summary>
    /// Initializes a copy of a set.
    /// </summary>
    /// <param name="configuredTypes">The set to copy.</param>
    public ConfiguredTypeSet( IConfiguredTypeSet configuredTypes )
    {
        _allTypes = new HashSet<ICachedType>( configuredTypes.AllTypes );
        _configuredTypes = new TypeConfigurationSet( configuredTypes.ConfiguredTypes );
    }

    /// <inheritdoc />
    public IReadOnlySet<ICachedType> AllTypes => _allTypes;

    /// <inheritdoc />
    public IReadOnlyCollection<TypeConfiguration> ConfiguredTypes => _configuredTypes;

    /// <summary>
    /// Adds types (not configured).
    /// </summary>
    /// <param name="types">The types to add.</param>
    public void Add( IEnumerable<ICachedType> types ) => _allTypes.UnionWith( types );

    /// <summary>
    /// Adds a type (not configured).
    /// </summary>
    /// <param name="type">The type to add.</param>
    /// <returns>True if it was added, false if it was already here.</returns>
    public bool Add( ICachedType type ) => _allTypes.Add( type );

    /// <summary>
    /// Removes a type, and its configuration if any.
    /// </summary>
    /// <param name="type">The type to remove.</param>
    /// <returns>True if it was here.</returns>
    // Beware! Only one | here, we want to remove from both of them.
    public bool Remove( ICachedType type ) => _allTypes.Remove( type ) | _configuredTypes.Remove( type.Type );

    /// <summary>
    /// Adds a type with its configured kind (<see cref="ConfigurableAutoServiceKind.None"/> adds it unconfigured).
    /// </summary>
    /// <param name="monitor">The monitor that logs a kind change.</param>
    /// <param name="sourceName">The source of this addition, for the log.</param>
    /// <param name="type">The type to add.</param>
    /// <param name="kind">Its configured kind.</param>
    /// <returns>True if the set changed.</returns>
    public bool Add( IActivityMonitor monitor, string sourceName, ICachedType type, ConfigurableAutoServiceKind kind )
    {
        if( kind == ConfigurableAutoServiceKind.None )
        {
            return _allTypes.Add( type );
        }
        if( _configuredTypes.AsDictionary.TryGetValue( type.Type, out var exists ) )
        {
            if( exists == kind ) return false;
            monitor.Info( $"{sourceName} updated '{type:N}' from '{exists}' to {kind}." );
        }
        _allTypes.Add( type );
        _configuredTypes.Add( type.Type, kind );
        CheckInvariants( this );
        return true;
    }

    /// <summary>
    /// Adds the types and the configurations of another set (its configured kinds win).
    /// </summary>
    /// <param name="monitor">The monitor that logs a kind change.</param>
    /// <param name="other">The set to add.</param>
    /// <param name="sourceName">The source of this addition, for the log.</param>
    public void Add( IActivityMonitor monitor, ConfiguredTypeSet other, string sourceName )
    {
        CheckInvariants( other );
        _allTypes.UnionWith( other.AllTypes );
        foreach( var o in other._configuredTypes.AsDictionary )
        {
            if( _configuredTypes.AsDictionary.TryGetValue( o.Key, out var exists ) && exists != o.Value )
            {
                monitor.Info( $"{sourceName} updated '{o.Key:N}' from '{exists}' to {o.Value}." );
            }
            _configuredTypes.Add( o.Key, o.Value );
        }
        CheckInvariants( this );
    }

    [Conditional( "DEBUG" )]
    static void CheckInvariants( ConfiguredTypeSet set )
    {
        // Cannot use the IsSupersetOf of set.AllTypes (of ICacheType) because we don't have the GlobalTypeCache here.
        // Hopefully, this is in Debug only.
        var types = new HashSet<Type>( set._allTypes.Select( cT => cT.Type ) );
        Throw.DebugAssert( types.IsSupersetOf( set._configuredTypes.AsDictionary.Keys ) );
        Throw.DebugAssert( !set._configuredTypes.AsDictionary.Values.Any( k => k == ConfigurableAutoServiceKind.None ) );
    }
}
