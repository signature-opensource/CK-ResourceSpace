using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CK.Setup;

/// <summary>
/// A named set of types of a BinPath, computed inside its parent (the <see cref="Parent"/> set, or the BinPath): see
/// <see cref="BinPathConfiguration.TypeSets"/>. A set holds membership only (no service kind: the kinds are a setting of
/// the BinPath's own types).
/// <list type="bullet">
///   <item>
///   Its packages (the concrete <c>IResourceGroup</c>) are the closure of its roots (their required relations) among the
///   packages of its parent minus its <see cref="ExcludedTypes"/> and <see cref="ExcludedFeatures"/>. The roots are the
///   packages of its <see cref="Types"/> and <see cref="Features"/>; with none, the non optional packages of its parent.
///   </item>
///   <item>Its other types are the ones of its parent minus its <see cref="ExcludedTypes"/>.</item>
///   <item>The <c>[AlsoRegisterType]</c> of its members are members.</item>
///   <item>
///   Its <see cref="Types"/> and its roots propagate upward: they are types of the BinPath and roots of its parent.
///   </item>
/// </list>
/// A local party, a client of a party: each one has its set (the structure of an application is compile time).
/// </summary>
public sealed class TypeSetConfiguration
{
    readonly string _name;
    readonly HashSet<Type> _types;
    readonly HashSet<Type> _excludedTypes;
    readonly HashSet<string> _features;
    readonly HashSet<string> _excludedFeatures;

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
        _features = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
        _excludedFeatures = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
    }

    internal TypeSetConfiguration( XElement e )
    {
        _name = (string)e.AttributeRequired( EngineConfiguration.xName );
        Parent = (string?)e.Attribute( EngineConfiguration.xParent );
        _types = new HashSet<Type>( EngineConfiguration.TypesFromXml( e, EngineConfiguration.xTypes, EngineConfiguration.xType ) );
        _excludedTypes = new HashSet<Type>( EngineConfiguration.TypesFromXml( e, EngineConfiguration.xExcludedTypes, EngineConfiguration.xType ) );
        _features = new HashSet<string>( EngineConfiguration.StringsFromXml( e, EngineConfiguration.xFeatures, EngineConfiguration.xFeature ),
                                         StringComparer.OrdinalIgnoreCase );
        _excludedFeatures = new HashSet<string>( EngineConfiguration.StringsFromXml( e, EngineConfiguration.xExcludedFeatures, EngineConfiguration.xFeature ),
                                                 StringComparer.OrdinalIgnoreCase );
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
    /// Gets the feature names (case insensitive) of the packages that are roots of this set: the names given by the
    /// <c>INamedFeatureAttribute</c> of the packages. A name only activates its packages: it is never an alias of a type.
    /// </summary>
    public HashSet<string> Features => _features;

    /// <summary>
    /// Gets the feature names (case insensitive) of the packages of the parent that are not members of this set.
    /// </summary>
    public HashSet<string> ExcludedFeatures => _excludedFeatures;

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
                             _excludedTypes.Count > 0 ? EngineConfiguration.ToXml( EngineConfiguration.xExcludedTypes, EngineConfiguration.xType, _excludedTypes ) : null,
                             _features.Count > 0 ? EngineConfiguration.ToXml( EngineConfiguration.xFeatures, EngineConfiguration.xFeature, _features ) : null,
                             _excludedFeatures.Count > 0 ? EngineConfiguration.ToXml( EngineConfiguration.xExcludedFeatures, EngineConfiguration.xFeature, _excludedFeatures ) : null );
    }

    /// <summary>
    /// Gets whether this set has the same name and content as another one.
    /// </summary>
    /// <param name="other">The other set.</param>
    /// <returns>True when they are equal.</returns>
    public bool SetEquals( TypeSetConfiguration other ) => _name == other._name
                                                           && Parent == other.Parent
                                                           && _types.SetEquals( other._types )
                                                           && _excludedTypes.SetEquals( other._excludedTypes )
                                                           && _features.SetEquals( other._features )
                                                           && _excludedFeatures.SetEquals( other._excludedFeatures );

    /// <summary>
    /// Gets a hash code of the name and the content of this set (see <see cref="SetEquals(TypeSetConfiguration)"/>).
    /// </summary>
    /// <returns>The hash code.</returns>
    public int GetContentHashCode()
    {
        int h = HashCode.Combine( _name, Parent );
        foreach( var t in _types ) h ^= t.GetHashCode();
        foreach( var t in _excludedTypes ) h ^= ~t.GetHashCode();
        foreach( var f in _features ) h ^= StringComparer.OrdinalIgnoreCase.GetHashCode( f );
        foreach( var f in _excludedFeatures ) h ^= ~StringComparer.OrdinalIgnoreCase.GetHashCode( f );
        return h;
    }

    /// <summary>
    /// Gets whether a string is a valid feature name: an identifier made of letters, digits and dots that starts with
    /// a letter (and doesn't end with a dot).
    /// </summary>
    /// <param name="name">The name to check.</param>
    /// <returns>True if it is valid.</returns>
    public static bool IsValidFeatureName( string? name )
    {
        if( string.IsNullOrEmpty( name ) || !char.IsAsciiLetter( name[0] ) || name[^1] == '.' ) return false;
        foreach( var c in name )
        {
            if( !char.IsAsciiLetterOrDigit( c ) && c != '.' ) return false;
        }
        return !name.Contains( ".." );
    }

    /// <summary>
    /// Returns "TypeSet 'Name'".
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"TypeSet '{_name}'";
}
