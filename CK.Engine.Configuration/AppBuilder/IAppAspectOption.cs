using CK.Core;

namespace CK.Setup;

/// <summary>
/// An option of an App aspect (see <see cref="IAppAspect"/>): a package configured in the section of its aspect, by its
/// name (declared by its <see cref="AppAspectOptionAttribute"/>), that applies to <typeparamref name="TAspect"/> and to
/// the aspects derived from it.
/// <para>
/// An option is a plain class that declares its data: its single public constructor reads its configuration (its optional
/// <c>Microsoft.Extensions.Configuration.IConfiguration</c> parameter, null when the option is enabled by default or by
/// <c>true</c>) into typed properties, and throws on an invalid value. The builder orders the options of an aspect (the
/// topology attributes between options order them; a non optional relation also means "only with it"), selects the
/// enabled ones from the section of the aspect, creates them and gives them to the aspect: its constructor receives them
/// in an <c>IReadOnlyList&lt;IAppAspectOption&lt;TAspect&gt;&gt;</c> parameter. What applying an option means is the
/// aspect's.
/// </para>
/// </summary>
/// <typeparam name="TAspect">The aspect type (or a base of it) to which this option applies.</typeparam>
public interface IAppAspectOption<in TAspect> : IConfigurableOption<TAspect> where TAspect : IAppAspect
{
}
