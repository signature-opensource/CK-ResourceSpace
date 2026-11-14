using CK.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CK.Setup;

/// <summary>
/// Models the &lt;Types&gt; element.
/// </summary>
public sealed class TypeConfigurationSet : IReadOnlyCollection<TypeConfiguration>
{
    readonly Dictionary<Type, ConfigurableAutoServiceKind> _types;

    /// <summary>
    /// Initializes a new empty <see cref="TypeConfigurationSet"/>.
    /// </summary>
    public TypeConfigurationSet()
    {
        _types = new Dictionary<Type, ConfigurableAutoServiceKind>();
    }

    /// <summary>
    /// Copy constructor. There must be no duplicate type in <paramref name="other"/>.
    /// </summary>
    /// <param name="other">The other set.</param>
    public TypeConfigurationSet( IReadOnlyCollection<TypeConfiguration> other )
    {
        Throw.CheckNotNullArgument( other );
        if( other is TypeConfigurationSet o )
        {
            _types = new Dictionary<Type, ConfigurableAutoServiceKind>( o._types );
        }
        else
        {
            _types = new Dictionary<Type, ConfigurableAutoServiceKind>( other.Count );
            foreach( var tc in other ) _types.Add( tc.Type, tc.Kind );
        }
    }

    internal TypeConfigurationSet( IEnumerable<XElement> e )
        : this()
    {
        foreach( var tc in e.Elements( EngineConfiguration.xType ).Select( t => new TypeConfiguration( t ) ) )
        {
            _types.Add( tc.Type, tc.Kind );
        }
    }

    internal XElement ToXml( XName name )
    {
        return new XElement( name,
                             _types.Select( kv => new XElement( EngineConfiguration.xType,
                                                                kv.Value != ConfigurableAutoServiceKind.None
                                                                    ? new XAttribute( EngineConfiguration.xKind, kv.Value )
                                                                    : null,
                                                                    kv.Key.GetWeakAssemblyQualifiedName() ) ) );
    }

    /// <inheritdoc />
    public int Count => _types.Count;

    /// <summary>
    /// Adds the <see cref="TypeConfiguration"/>, replacing any existing configuration with the same <see cref="TypeConfiguration.Type"/>.
    /// </summary>
    /// <param name="configuration">The configuration to add.</param>
    /// <returns>This set.</returns>
    public TypeConfigurationSet Add( TypeConfiguration configuration )
    {
        _types[configuration.Type] = configuration.Kind;
        return this;
    }

    /// <summary>
    /// Adds the type association, replacing any existing configuration with the same <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The type to configure.</param>
    /// <param name="kind">The kind of the type.</param>
    /// <returns>This set.</returns>
    public TypeConfigurationSet Add( Type type, ConfigurableAutoServiceKind kind )
    {
        _types[type] = kind;
        return this;
    }

    /// <summary>
    /// Adds that the type whith <see cref="ConfigurableAutoServiceKind.None"/> if it doesn't already
    /// exist but don't change its <see cref="TypeConfiguration.Kind"/> if it exists.
    /// </summary>
    /// <param name="type">The type to add.</param>
    /// <returns>This set.</returns>
    public TypeConfigurationSet Add( Type type )
    {
        _types.TryAdd( type, ConfigurableAutoServiceKind.None );
        return this;
    }

    /// <summary>
    /// Gets this set as a dictionary.
    /// </summary>
    public IDictionary<Type, ConfigurableAutoServiceKind> AsDictionary => _types;

    /// <summary>
    /// Updates this set from the content of other one. <paramref name="other"/> configurations
    /// replace any current ones: this is used to apply <see cref="EngineConfiguration.GlobalTypes"/>
    /// to each <see cref="BinPathConfiguration.Types"/>.
    /// </summary>
    /// <param name="other">The set to merge.</param>
    internal void ApplyMerge( TypeConfigurationSet other )
    {
        foreach( var kv in other._types )
        {
            _types[kv.Key] = kv.Value;
        }
    }

    /// <summary>
    /// Removes a configuration.
    /// </summary>
    /// <param name="type">The type to remove.</param>
    /// <returns>True if it has been removed, false if it doesn't exist.</returns>
    public bool Remove( Type type ) => _types.Remove( type );

    /// <summary>
    /// Check if this set contains the same and only the same elements as other.
    /// </summary>
    /// <param name="other">The other set.</param>
    /// <returns>True if this set contains the same configurations as the other one.</returns>
    public bool SetEquals( TypeConfigurationSet other )
    {
        if( _types.Count == other._types.Count )
        {
            foreach( var kv in _types )
            {
                if( !other._types.TryGetValue( kv.Key, out var oV ) || kv.Value != oV )
                {
                    return false;
                }
            }
            return true;
        }
        return false;
    }

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

    public struct Enumerator : IEnumerator<TypeConfiguration>
    {
        // Don't make this readonly or nothing will work!
        Dictionary<Type, ConfigurableAutoServiceKind>.Enumerator _e;

        internal Enumerator( Dictionary<Type, ConfigurableAutoServiceKind>.Enumerator e ) => _e = e;

        public TypeConfiguration Current => new TypeConfiguration( _e.Current.Key, _e.Current.Value );

        object IEnumerator.Current => Current;

        public void Dispose() => _e.Dispose();

        public bool MoveNext() => _e.MoveNext();

        public void Reset() => _e.MoveNext();
    }

    public Enumerator GetEnumerator() => new Enumerator( _types.GetEnumerator() );

    IEnumerator<TypeConfiguration> IEnumerable<TypeConfiguration>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
