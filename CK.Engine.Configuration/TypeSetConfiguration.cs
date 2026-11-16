using CK.Core;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CK.Setup;

/// <summary>
/// A named set of types of a BinPath, computed inside its parent (the <see cref="Parent"/> set, or the BinPath): see
/// <see cref="BinPathConfiguration.TypeSets"/>. A set holds membership only (no service kind: the kinds are a setting of
/// the BinPath's own types).
/// <list type="bullet">
///   <item>
///   Its packages (the concrete <c>IResourceGroup</c>) are the closure of its roots (their required relations) among the
///   packages of its parent minus its <see cref="ExcludedTypes"/>. The roots are the packages of its <see cref="Types"/>;
///   with none, the non optional packages of its parent.
///   </item>
///   <item>Its other types are the ones of its parent minus its <see cref="ExcludedTypes"/>.</item>
///   <item>The <c>[AlsoRegisterType]</c> of its members are members.</item>
///   <item>
///   Its <see cref="Types"/> and its roots propagate upward: they are types of the BinPath and roots of its parent.
///   </item>
/// </list>
/// A local party, a client of a party: each one has its set (the structure of an application is compile time).
/// The types drive: the configuration names of the configurable types (<c>IConfigurableTypeAttribute</c>) are the keys
/// of their sections, they never bring a type in a set.
/// </summary>
public sealed class TypeSetConfiguration
{
    readonly string _name;
    readonly HashSet<Type> _types;
    readonly HashSet<Type> _excludedTypes;

    /// <summary>
    /// Initializes a new empty set.
    /// </summary>
    /// <param name="name">The name of the set: unique in its BinPath.</param>
    public TypeSetConfiguration( string name )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        _name = name;
        _types = new HashSet<Type>();
        _excludedTypes = new HashSet<Type>();
    }

    internal TypeSetConfiguration( XElement e )
    {
        _name = (string)e.AttributeRequired( EngineConfiguration.xName );
        Parent = (string?)e.Attribute( EngineConfiguration.xParent );
        _types = new HashSet<Type>( EngineConfiguration.TypesFromXml( e, EngineConfiguration.xTypes, EngineConfiguration.xType ) );
        _excludedTypes = new HashSet<Type>( EngineConfiguration.TypesFromXml( e, EngineConfiguration.xExcludedTypes, EngineConfiguration.xType ) );
    }

    /// <summary>
    /// Gets the name of this set: unique in its BinPath.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// Gets or sets the name of the parent set of this one (the party of a client): this set is computed inside it (its
    /// closure is resolved among its members). Null when the parent is the BinPath.
    /// </summary>
    public string? Parent { get; set; }

    /// <summary>
    /// Gets the types that this set adds: they are members of this set and of the BinPath. Its packages are roots of
    /// the set.
    /// </summary>
    public HashSet<Type> Types => _types;

    /// <summary>
    /// Gets the types of the parent that are not members of this set, with everything that is one of them (see
    /// <see cref="TypeExclusion"/>).
    /// </summary>
    public HashSet<Type> ExcludedTypes => _excludedTypes;

    /// <summary>
    /// Creates a xml element from this set.
    /// </summary>
    /// <returns>A new element.</returns>
    public XElement ToXml()
    {
        return new XElement( EngineConfiguration.xTypeSet,
                             new XAttribute( EngineConfiguration.xName, _name ),
                             Parent != null ? new XAttribute( EngineConfiguration.xParent, Parent ) : null,
                             _types.Count > 0 ? EngineConfiguration.ToXml( EngineConfiguration.xTypes, EngineConfiguration.xType, _types ) : null,
                             _excludedTypes.Count > 0 ? EngineConfiguration.ToXml( EngineConfiguration.xExcludedTypes, EngineConfiguration.xType, _excludedTypes ) : null );
    }

    /// <summary>
    /// Gets whether this set has the same name and content as another one.
    /// </summary>
    /// <param name="other">The other set.</param>
    /// <returns>True when they are equal.</returns>
    public bool SetEquals( TypeSetConfiguration other ) => _name == other._name
                                                           && Parent == other.Parent
                                                           && _types.SetEquals( other._types )
                                                           && _excludedTypes.SetEquals( other._excludedTypes );

    /// <summary>
    /// Gets a hash code of the name and the content of this set (see <see cref="SetEquals(TypeSetConfiguration)"/>).
    /// </summary>
    /// <returns>The hash code.</returns>
    public int GetContentHashCode()
    {
        int h = HashCode.Combine( _name, Parent );
        foreach( var t in _types ) h ^= t.GetHashCode();
        foreach( var t in _excludedTypes ) h ^= ~t.GetHashCode();
        return h;
    }

    /// <summary>
    /// Returns "TypeSet 'Name'".
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"TypeSet '{_name}'";
}
