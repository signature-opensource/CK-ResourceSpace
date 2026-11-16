using CK.Setup;
using System;
using System.Collections.Generic;

namespace CK.TypeCache;

/// <summary>
/// Final sets of types once types inclusion/exclusion have been processed.
/// <see cref="ConfigurableAutoServiceKind"/> in <see cref="ConfiguredTypes"/> is settled and final kind
/// detection can now be executed based on configuration.
/// <para>
/// The initial set is the <see cref="AssemblyCache.BinPathGroup.ConfiguredTypes"/> (computed from
/// the <see cref="AssemblyCache.BinPathGroup.HeadAssemblies"/>).
/// </para>
/// <para>
/// The final set is the ConfiguredTypes of the BinPathTypeGroup (CK.Engine.TypeSet) that applied its <see cref="BinPathConfiguration.Types"/>
/// and <see cref="BinPathConfiguration.ExcludedTypes"/> to the initial one.
/// </para>
/// </summary>
public interface IConfiguredTypeSet
{
    /// <summary>
    /// Gets all the types that must be registered, including the ones in <see cref="ConfiguredTypes"/>.
    /// </summary>
    IReadOnlySet<ICachedType> AllTypes { get; }

    /// <summary>
    /// Gets the type configurations with a <see cref="TypeConfiguration.Kind"/> that is not <see cref="ConfigurableAutoServiceKind.None"/>.
    /// <para>
    /// TypeConfiguration uses <see cref="Type"/>, not <see cref="ICachedType"/> because it comes from the configuration.
    /// </para>
    /// </summary>
    IReadOnlyCollection<TypeConfiguration> ConfiguredTypes { get; }
}
