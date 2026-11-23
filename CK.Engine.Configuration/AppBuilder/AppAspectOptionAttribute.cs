using CK.Core;
using System;
using System.Runtime.CompilerServices;

namespace CK.Setup;

/// <summary>
/// Decorates an option of an App aspect: an <see cref="IAppAspectOption{TAspect}"/>. Its name is its key in the section
/// of its aspect (<c>[AppAspectOption( "X", TypeActivation.Optional )]</c> on an option of the "Globalization" aspect is
/// <c>"Aspects": { "Globalization": { "X": true } }</c>).
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = false, Inherited = false )]
public sealed class AppAspectOptionAttribute : Attribute, IConfigurableOptionAttribute
{
    /// <summary>
    /// Initializes a new <see cref="AppAspectOptionAttribute"/>.
    /// </summary>
    /// <param name="name">The configuration name of the option: its key in the section of its aspect.</param>
    /// <param name="activation">The activation of the option.</param>
    /// <param name="callerFilePath">Automatically set by the compiler.</param>
    public AppAspectOptionAttribute( string name, TypeActivation activation, [CallerFilePath] string? callerFilePath = null )
    {
        Name = name;
        Activation = activation;
        CallerFilePath = callerFilePath;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public TypeActivation Activation { get; }

    /// <inheritdoc />
    public string? CallerFilePath { get; }

    /// <summary>
    /// Gets <see cref="IAppAspect"/>.
    /// </summary>
    public Type OwnerKind => typeof( IAppAspect );
}
