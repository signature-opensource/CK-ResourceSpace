using System.Collections.Immutable;

namespace CK.Core;

/// <summary>
/// Captures a culture in a <see cref="ActiveCultureSet"/>.
/// </summary>
public sealed class ActiveCulture
{
    readonly NormalizedCultureInfo _c;
    readonly ActiveCultureSet _set;
    readonly int _index;
    readonly ActiveCulture? _parent;
    readonly ImmutableArray<ActiveCulture> _path;
    internal ImmutableArray<ActiveCulture> _children;

    internal ActiveCulture( ActiveCultureSet set, NormalizedCultureInfo c, int index, ActiveCulture? parent, ImmutableArray<ActiveCulture> path )
    {
        _c = c;
        _set = set;
        _index = index;
        _parent = parent;
        _path = path;
    }

    /// <summary>
    /// Gets the culture.
    /// </summary>
    public NormalizedCultureInfo Culture => _c;

    /// <summary>
    /// Gets the active culture set to which this active culture belongs.
    /// </summary>
    public ActiveCultureSet ActiveCultures => _set;

    /// <summary>
    /// Gets a unique index in the <see cref="ActiveCultureSet.AllActiveCultures"/>.
    /// <para>
    /// This index must be used to easily and efficiently associate any external data
    /// to active cultures (by using an array of external data).
    /// </para>
    /// </summary>
    public int Index => _index;

    /// <summary>
    /// Gets the parent culture. Null if this is the <see cref="ActiveCultureSet.Root"/>.
    /// </summary>
    public ActiveCulture? Parent => _parent;

    /// <summary>
    /// Gets the path from the <see cref="ActiveCultureSet.Root"/> up to (but not included)
    /// this active culture.
    /// </summary>
    public ImmutableArray<ActiveCulture> Path => _path;

    /// <summary>
    /// Gets the children if any.
    /// </summary>
    public ImmutableArray<ActiveCulture> Children => _children;

    /// <summary>
    /// Returns the <see cref="Culture"/> name.
    /// </summary>
    /// <returns>The culture name.</returns>
    public override string ToString() => _c.ToString();
}
