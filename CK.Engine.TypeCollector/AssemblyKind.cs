using CK.Setup;
using System;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Categorizes discovered assemblies.
/// </summary>
[Flags]
public enum AssemblyKind
{
    /// <summary>
    /// This assembly is unknown or non relevant: it references no <see cref="PFeatureDefiner"/> nor <see cref="PFeature"/>
    /// and is not a CKEngine.
    /// </summary>
    None,

    /// <summary>
    /// This assembly has been skipped. It is a system assembly that we totally ignore.
    /// </summary>
    SystemSkipped = 1,

    /// <summary>
    /// This assembly has been skipped. It is a system assembly that we totally ignore.
    /// </summary>
    AutoSkipped = 2,

    /// <summary>
    /// This assembly has been excluded by configuration.
    /// </summary>
    Excluded = 4,

    /// <summary>
    /// This assembly is on the engine side.
    /// </summary>
    Engine = 8,

    /// <summary>
    /// This is a definer assembly (it is marked with a <see cref="IsPFeatureAttribute"/>).
    /// Assemblies that reference it are <see cref="PFeature"/>.
    /// </summary>
    PFeatureDefiner = 16,

    /// <summary>
    /// The assembly's visible types will be registered: it is either marked with a <see cref="IsPFeatureAttribute"/>
    /// or depends (at any depth) on another PFeature or an assembly that is marked with a <see cref="IsPFeatureDefinerAttribute"/>.
    /// </summary>
    PFeature = 32
}

/// <summary>
/// Encapsulates bit flags operations for <see cref="AssemblyKind"/>.
/// </summary>
public static class AssemblyKindExtensions
{
    /// <summary>
    /// Gets whether this assembly is <see cref="AssemblyKind.AutoSkipped"/> or <see cref="AssemblyKind.SystemSkipped"/>.
    /// </summary>
    /// <param name="kind">This kind.</param>
    /// <returns>Whether this assembly is skipped.</returns>
    public static bool IsSkipped( this AssemblyKind kind ) => (kind & (AssemblyKind.SystemSkipped | AssemblyKind.AutoSkipped)) != 0;

    /// <summary>
    /// Gets whether this assembly is <see cref="AssemblyKind.Excluded"/>.
    /// </summary>
    /// <param name="kind">This kind.</param>
    /// <returns>Whether this assembly is excluded.</returns>
    public static bool IsExcluded( this AssemblyKind kind ) => (kind & AssemblyKind.Excluded) != 0;

    /// <summary>
    /// Not yet categorized assembly.
    /// </summary>
    /// <param name="kind">This kind.</param>
    /// <returns>Whether this assembly kind is not known.</returns>
    public static bool IsNone( this AssemblyKind kind ) => (kind & ~AssemblyKind.Excluded) == 0;

    /// <summary>
    /// Gets whether this assembly is <see cref="AssemblyKind.PFeature"/>.
    /// </summary>
    /// <param name="kind">This kind.</param>
    /// <returns>Whether this assembly is a PFeature.</returns>
    public static bool IsPFeature( this AssemblyKind kind ) => (kind & AssemblyKind.PFeature) != 0;

    /// <summary>
    /// Gets whether this assembly is <see cref="AssemblyKind.PFeatureDefiner"/>.
    /// </summary>
    /// <param name="kind">This kind.</param>
    /// <returns>Whether this assembly is a PFeature definer.</returns>
    public static bool IsPFeatureDefiner( this AssemblyKind kind ) => (kind & AssemblyKind.PFeatureDefiner) != 0;

    /// <summary>
    /// Gets whether this assembly is <see cref="AssemblyKind.Engine"/>.
    /// </summary>
    /// <param name="kind">This kind.</param>
    /// <returns>Whether this assembly is an engine.</returns>
    public static bool IsEngine( this AssemblyKind kind ) => (kind & AssemblyKind.Engine) != 0;

    /// <summary>
    /// Gets whether this assembly is <see cref="AssemblyKind.PFeature"/> or <see cref="AssemblyKind.PFeatureDefiner"/>.
    /// </summary>
    /// <param name="kind">This kind.</param>
    /// <returns>Whether this assembly is a feature or a feature definer.</returns>
    public static bool IsPFeatureOrDefiner( this AssemblyKind kind ) => (kind & (AssemblyKind.PFeature | AssemblyKind.PFeatureDefiner)) != 0;

    /// <summary>
    /// Gets wether this kind is an excluded engine: this is a warning and Excluded is removed.
    /// </summary>
    /// <param name="kind">This kind.</param>
    /// <returns>Whether this assembly is currently an excluded engine.</returns>
    public static bool IsExcludedEngine( this AssemblyKind kind ) => (kind & (AssemblyKind.Engine | AssemblyKind.Excluded)) == (AssemblyKind.Engine | AssemblyKind.Excluded);

    /// <summary>
    /// Gets wether this kind is an excluded PFeature definer: this is a warning and Excluded is removed.
    /// </summary>
    /// <param name="kind">This kind.</param>
    /// <returns>Whether this assembly is currently an excluded PFeature definer.</returns>
    public static bool IsExcludedPFeatureDefiner( this AssemblyKind kind ) => (kind & (AssemblyKind.PFeatureDefiner | AssemblyKind.Excluded)) == (AssemblyKind.PFeatureDefiner | AssemblyKind.Excluded);

    /// <summary>
    /// Clears <see cref="AssemblyKind.PFeature"/> and <see cref="AssemblyKind.PFeatureDefiner"/> and sets <see cref="AssemblyKind.Engine"/>.
    /// </summary>
    /// <param name="k">This kind.</param>
    /// <returns>Returns an engine kind.</returns>
    public static AssemblyKind SetEngine( this AssemblyKind k )
    {
        k &= AssemblyKind.PFeature | AssemblyKind.PFeatureDefiner;
        return k | AssemblyKind.Engine;
    }

    /// <summary>
    /// Clears <see cref="AssemblyKind.PFeatureDefiner"/> and <see cref="AssemblyKind.Engine"/> and sets <see cref="AssemblyKind.PFeature"/>.
    /// </summary>
    /// <param name="k">This kind.</param>
    /// <returns>Returns an engine kind.</returns>
    public static AssemblyKind SetPFeature( this AssemblyKind k )
    {
        k &= AssemblyKind.Engine | AssemblyKind.PFeatureDefiner;
        return k | AssemblyKind.PFeature;
    }
}
