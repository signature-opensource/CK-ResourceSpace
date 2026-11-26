using CK.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CK.Setup;

/// <summary>
/// The "Aspects" of the "ckonf.json" of a builder: the AppContext aspects (at its root, <see cref="AppBuilderContext.Aspects"/>)
/// or the aspects of one App (<see cref="AppConfiguration.Aspects"/>). They are compile time: they are never projected as
/// such in the hosts' "appsettings.json".
/// <para>
/// An aspect is a type of a model (<see cref="AppContextAspectAttribute"/>, <see cref="AppAspectAttribute"/>): the builder
/// reads its section (<see cref="TryGetAspect(string)"/>) when it finds the aspect in the type sets of the Apps; then an
/// aspect key that names no aspect is an error ("no package handles the aspect 'X'"), before the contributors run.
/// </para>
/// </summary>
public sealed class AppAspects
{
    readonly ImmutableConfigurationSection? _section;
    readonly HashSet<string> _handled;

    /// <summary>
    /// Initializes a new set of aspects. The builder creates them from "ckonf.json"; this is public for the tests.
    /// </summary>
    /// <param name="section">The "Aspects" section, null when there is none.</param>
    public AppAspects( ImmutableConfigurationSection? section )
    {
        _section = section;
        _handled = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
    }

    /// <summary>
    /// Gets the "Aspects" section. Null when there is none.
    /// </summary>
    public ImmutableConfigurationSection? Section => _section;

    /// <summary>
    /// Gets the configuration of an aspect and marks it as handled. Used by the builder: an aspect receives its section
    /// in its constructor.
    /// </summary>
    /// <param name="name">The aspect name ("Globalization", "SqlServer"...).</param>
    /// <returns>Its configuration, or null when it is not configured.</returns>
    public ImmutableConfigurationSection? TryGetAspect( string name )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        var s = _section?.TryGetSection( name );
        if( s != null ) _handled.Add( s.Key );
        return s;
    }

    /// <summary>
    /// Gets the configured aspects that no contributor read (by <see cref="TryGetAspect(string)"/>).
    /// </summary>
    public IEnumerable<ImmutableConfigurationSection> UnhandledAspects => _section?.GetChildren().Where( c => !_handled.Contains( c.Key ) ) ?? Enumerable.Empty<ImmutableConfigurationSection>();
}
