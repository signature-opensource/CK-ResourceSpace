using CK.Core;
using System.Xml.Linq;

namespace CK.Setup;

/// <summary>
/// Base class for configuration of a Engine Aspect.
/// Aspect configuration must have a deserialization constructor that takes a XElement.
/// It is highly recommended to support a <see cref="EngineConfiguration.xVersion"/> attribute
/// to ease and secure any configuration evolution. 
/// </summary>
public abstract class EngineAspectConfiguration
{
    readonly string _name;

    /// <summary>
    /// Initializes an empty aspect configuration.
    /// </summary>
    protected EngineAspectConfiguration()
    {
        Throw.DebugAssert( "AspectConfiguration".Length == 19 );
        var n = GetType().Name;
        if( n.Length <= 19 || !n.EndsWith( "AspectConfiguration" ) )
        {
            Throw.CKException( $"Invalid aspect configuration type name '{GetType().Name}': it must end with \"AspectConfiguration\"." );
        }
        _name = n.Substring( 0, n.Length - 19 );
    }

    /// <summary>
    /// Initializes a new aspect from a <see cref="XElement"/>.
    /// </summary>
    /// <param name="e">The xml element.</param>
    protected EngineAspectConfiguration( XElement e )
        : this()
    {
    }

    /// <summary>
    /// Gets this aspect name: this is the type name without the "AspectConfiguration" suffix.
    /// </summary>
    public string AspectName => _name;

    /// <summary>
    /// Gets the configuration that contains this aspect in its <see cref="EngineConfiguration.Aspects"/>.
    /// </summary>
    public EngineConfiguration? Owner { get; internal set; }

    /// <summary>
    /// Gets the fully qualified name of the type on the Engine side that implements this aspect.
    /// </summary>
    public abstract string AspectType { get; }

    /// <summary>
    /// Must serialize its content in the provided <see cref="XElement"/> and returns it.
    /// The dedicated constructor will be able to read this element back.
    /// Note that a Type attribute (that contains this aspect configuration Type name) is automatically injected.
    /// </summary>
    /// <param name="e">The element to populate.</param>
    /// <returns>The <paramref name="e"/> element.</returns>
    public abstract XElement SerializeXml( XElement e );

    /// <summary>
    /// Factory method for specific <see cref="BinPathAspectConfiguration"/>.
    /// Not all aspects have such specific configuration: by default this throws a <see cref="System.IO.InvalidDataException"/>.
    /// </summary>
    /// <returns>A new empty BinPath specific configuration for this aspect.</returns>
    public virtual BinPathAspectConfiguration CreateBinPathConfiguration()
    {
        return Throw.InvalidDataException<BinPathAspectConfiguration>( $"Aspect '{_name}' doesn't expect BinPath specific configuration. Did you forget to override CreateBinPathConfiguration()?" );
    }
}
