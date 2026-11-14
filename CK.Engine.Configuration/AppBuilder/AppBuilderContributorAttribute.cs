using CK.Core;
using System;

namespace CK.Setup;

/// <summary>
/// Declares a contributor of the CKomposableAppBuilder (CK.AppBuilder) (an <see cref="IAppBuilderContributor"/>) in an
/// assembly that the builder references (typically an engine). The builder finds it in its own folder: nothing has to be
/// written in the "XXX.Builder" project.
/// </summary>
[AttributeUsage( AttributeTargets.Assembly, AllowMultiple = true )]
public sealed class AppBuilderContributorAttribute : Attribute
{
    /// <summary>
    /// Initializes a new <see cref="AppBuilderContributorAttribute"/>.
    /// </summary>
    /// <param name="contributorType">The <see cref="IAppBuilderContributor"/> type.</param>
    public AppBuilderContributorAttribute( Type contributorType )
    {
        Throw.CheckNotNullArgument( contributorType );
        ContributorType = contributorType;
    }

    /// <summary>
    /// Gets the <see cref="IAppBuilderContributor"/> type.
    /// </summary>
    public Type ContributorType { get; }
}
