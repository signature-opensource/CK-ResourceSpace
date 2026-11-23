using CK.Core;

namespace CK.Setup;

/// <summary>
/// An option of a Builder aspect (see <see cref="IAppBuilderAspect"/>): a package configured in the section of its
/// aspect, by its name (declared by its <see cref="AppBuilderAspectOptionAttribute"/>), that applies to
/// <typeparamref name="TAspect"/> and to the aspects derived from it.
/// <para>
/// The rules are the ones of the App aspect options (see <see cref="IAppAspectOption{TAspect}"/>): the constructor of
/// the aspect receives its options in an <c>IReadOnlyList&lt;IAppBuilderAspectOption&lt;TAspect&gt;&gt;</c> parameter.
/// </para>
/// </summary>
/// <typeparam name="TAspect">The aspect type (or a base of it) to which this option applies.</typeparam>
public interface IAppBuilderAspectOption<in TAspect> : IConfigurableOption<TAspect> where TAspect : IAppBuilderAspect
{
}
