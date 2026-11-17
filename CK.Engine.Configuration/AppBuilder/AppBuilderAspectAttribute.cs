using CK.Core;
using System;

namespace CK.Setup;

/// <summary>
/// Declares a Builder aspect: an <see cref="IAppBuilderAspect"/> package that the root <c>"Aspects"</c> of the
/// "ckonf.json" of a builder names (<see cref="Name"/>), configures (its section) and switches (<see cref="Activation"/>).
/// A Builder aspect is engine wide (one per engine run): it is present when it is a member of the type set of one of
/// the Apps of the builder (<see cref="TypeActivation.ByDefault"/> and <see cref="TypeActivation.Required"/>) or
/// when its key is declared (<see cref="TypeActivation.Optional"/>), unless its key is <c>false</c>.
/// </summary>
[AttributeUsage( AttributeTargets.Class, AllowMultiple = false, Inherited = false )]
public sealed class AppBuilderAspectAttribute : Attribute, IConfigurableTypeAttribute
{
    /// <summary>
    /// Initializes a new <see cref="AppBuilderAspectAttribute"/>.
    /// </summary>
    /// <param name="name">The configuration name: the key of the aspect in the root <c>"Aspects"</c>.</param>
    /// <param name="activation">The activation of the aspect.</param>
    public AppBuilderAspectAttribute( string name, TypeActivation activation )
    {
        Name = name;
        Activation = activation;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public TypeActivation Activation { get; }
}
