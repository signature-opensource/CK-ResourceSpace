using CK.Core;
using System;

namespace CK.Setup;

/// <summary>
/// Declares an App aspect: an <see cref="IAppAspect"/> package that the <c>"Aspects"</c> of an App of the "ckonf.json"
/// of a builder (<c>"Apps:X:Aspects"</c>) names (<see cref="Name"/>), configures (its section) and switches
/// (<see cref="Activation"/>). An App aspect is present in an App when it is a member of its type set
/// (<see cref="TypeActivation.ByDefault"/> and <see cref="TypeActivation.Required"/>) or when its key is declared
/// (<see cref="TypeActivation.Optional"/>), unless its key is <c>false</c>.
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = false, Inherited = false )]
public sealed class AppAspectAttribute : Attribute, IConfigurableTypeAttribute
{
    /// <summary>
    /// Initializes a new <see cref="AppAspectAttribute"/>.
    /// </summary>
    /// <param name="name">The configuration name: the key of the aspect in the <c>"Aspects"</c> of an App.</param>
    /// <param name="activation">The activation of the aspect.</param>
    public AppAspectAttribute( string name, TypeActivation activation )
    {
        Name = name;
        Activation = activation;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public TypeActivation Activation { get; }
}
