namespace CK.Core;

/// <summary>
/// Handles automatic registration of a <see cref="ResPackageDescriptor"/>.
/// This controls the closure of the non-optional references between packages.
/// </summary>
public interface IResPackageDescriptorResolver
{
    /// <summary>
    /// Must register a required reference to a package. This method is called once and only once per non registered, non optional
    /// reference:
    /// <list type="bullet">
    ///     <item><see cref="ResPackageDescriptor.Ref.IsOptional"/> is false.</item>
    ///     <item><see cref="ResPackageDescriptor.Ref.AsPackageDescriptor"/> is null.</item>
    ///     <item><see cref="ResPackageDescriptor.Ref.IsValid"/> is true.</item>
    ///     <item>
    ///         <see cref="ResPackageDescriptor.Ref.AsType"/> or <see cref="ResPackageDescriptor.Ref.AsCachedType"/> can be not null:
    ///         a type bound package must be registered.
    ///     </item>
    ///     <item>
    ///         <see cref="ResPackageDescriptor.Ref.FullName"/> is never null: it is the type's full name or the named package
    ///         that must be registered.
    ///     </item>
    /// </list>
    /// <para>
    /// Nothing prevents more than one package to be registered (this may be useful in some scenario).
    /// <para>
    /// After this call, the <paramref name="reference"/> is checked to actually be registered (even if this
    /// method did nothing and returns true).
    /// </para>
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor that must receive detailed errors if the package cannot be registered.</param>
    /// <param name="registrar">The package registrar.</param>
    /// <param name="reference">The reference to resolve.</param>
    /// <returns>True on success, false on error. Errors must be logged.</returns>
    bool ResolveRequired( IActivityMonitor monitor, IResPackageDescriptorRegistrar registrar, ResPackageDescriptor.Ref reference );
}
