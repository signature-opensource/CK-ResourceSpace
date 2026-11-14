using CK.Core;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CK.Setup;

/// <summary>
/// BinPath specific configuration of a <see cref="EngineAspectConfiguration"/>.
/// Not all aspects need a per BinPath configuration.
/// <para>
/// If more than one configuration can exist for the same BinPath (this is the case of the &lt;TypeScript&gt; aspect),
/// then <see cref="MultipleBinPathAspectConfiguration{TSelf}"/> must be used.
/// </para>
/// </summary>
public abstract class BinPathAspectConfiguration
{
    readonly string _name;
    EngineAspectConfiguration? _aspectConfiguration;
    BinPathConfiguration? _owner;

    /// <summary>
    /// Initializes an empty BinPath aspect configuration.
    /// </summary>
    protected BinPathAspectConfiguration()
    {
        Throw.DebugAssert( "BinPathAspectConfiguration".Length == 26 );
        var n = GetType().Name;
        if( n.Length <= 26 || !n.EndsWith( "BinPathAspectConfiguration" ) )
        {
            Throw.CKException( $"Invalid BinPath aspect configuration type name '{GetType().Name}': it must end with \"BinPathAspectConfiguration\"." );
        }
        _name = n.Substring( 0, n.Length - 26 );
    }

    /// <summary>
    /// Gets the aspect name: this is the type name without the "BinPathAspectConfiguration" suffix.
    /// </summary>
    public string AspectName => _name;

    /// <summary>
    /// Gets the aspect configuration if this instance belongs to a <see cref="BinPathConfiguration.Aspects"/>,
    /// null otherwise.
    /// </summary>
    public EngineAspectConfiguration? AspectConfiguration => _aspectConfiguration;

    /// <summary>
    /// Gets the <see cref="BinPathConfiguration"/> to which this aspect belongs.
    /// </summary>
    public BinPathConfiguration? Owner => _owner;

    internal virtual void Bind( BinPathConfiguration? o, EngineAspectConfiguration? a )
    {
        _owner = o;
        _aspectConfiguration = a;
    }

    /// <summary>
    /// Rehydrates this instance from the xml element.
    /// </summary>
    /// <param name="e">The xml element to read.</param>
    public abstract void InitializeFrom( XElement e );

    /// <summary>
    /// Creates an Xml element with this configuration values: <see cref="WriteXml(XElement)"/> must be overridden.
    /// </summary>
    /// <returns>The xml element.</returns>
    public XElement ToXml()
    {
        var e = new XElement( _name );
        WriteXml( e );
        return e;
    }

    /// <summary>
    /// Adds to the provided <see cref="XElement"/> attributes and elements that <see cref="InitializeFrom(XElement)"/>
    /// will be able to read back.
    /// </summary>
    /// <param name="e">The element to populate.</param>
    protected abstract void WriteXml( XElement e );

    // This is required to support MultipleBinPathAspectConfiguration.
    internal virtual void HandleOwnRemove( Dictionary<string, BinPathAspectConfiguration> aspects )
    {
        // For regular aspect, no question ask: unbind and remove the aspect from the dictionary.
        // It is more complex for composite.
        Bind( null, null );
        aspects.Remove( AspectName );
    }
}
