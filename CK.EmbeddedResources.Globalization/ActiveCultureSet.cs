using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;

namespace CK.Core;

/// <summary>
/// Helper that captures a compact tree of <see cref="ActiveCulture"/>.
/// All parent cultures are guaranteed to exist.
/// <para>
/// Serialization of cultures cannot be straightforward: depending on the context, we may want to
/// <see cref="NormalizedCultureInfo.EnsureNormalizedCultureInfo(string)"/> or, more safely, to only
/// <see cref="AllCultureSnapshot.FindNormalizedCultureInfo(string)"/> on <see cref="ExtendedCultureInfo.All"/>.
/// For this <see cref="ActiveCultureSet"/>, the only really interesting serialization is the one that
/// preserves the <see cref="ActiveCulture.Index"/>: this must use a <c>EnsureNormalizedCultureInfo</c>
/// call to restore the state.
/// </para>
/// <para>
/// To prevent bad use of this API, active cultures are not directly serializable,
/// serialization of a set must be explicitly handled by storing the <see cref="AllActiveCultures"/>'s
/// <see cref="ExtendedCultureInfo.Name">culture names</see> in the array order (note that <see cref="ToString()"/>
/// does the job) and restore the set by providing the ensured NormalizedCultureInfo list to the constructor: index
/// will be correctly restored.
/// </para>
/// </summary>
public sealed class ActiveCultureSet
{
    readonly Dictionary<NormalizedCultureInfo, ActiveCulture> _index;
    readonly ImmutableArray<ActiveCulture> _all;

    /// <summary>
    /// Initializes a new <see cref="ActiveCultureSet"/>.
    /// It will alwats contain its <see cref="Root"/> even if <paramref name="cultures"/>
    /// is empty.
    /// </summary>
    /// <param name="cultures">The selection of cultures.</param>
    public ActiveCultureSet( IEnumerable<NormalizedCultureInfo> cultures )
    {
        var index = new Dictionary<NormalizedCultureInfo, ActiveCulture>();
        var all = new List<ActiveCulture>();
        var root = new ActiveCulture( this, NormalizedCultureInfo.CodeDefault, 0, null, ImmutableArray<ActiveCulture>.Empty );
        var rootPath = ImmutableCollectionsMarshal.AsImmutableArray( [root] );
        index.Add( NormalizedCultureInfo.CodeDefault, root );
        all.Add( root );
        foreach( var c in cultures )
        {
            if( !index.ContainsKey( c ) )
            {
                Register( index, all, rootPath, c );
            }
        }
        List<ActiveCulture>?[] children = new List<ActiveCulture>[index.Count];
        foreach( var c in all )
        {
            var p = c.Parent;
            if( p != null )
            {
                ref var child = ref children[p.Index];
                child ??= new List<ActiveCulture>();
                child.Add( c );
            }
        }
        for( int i = 0; i < children.Length; ++i )
        {
            var collected = children[i];
            all[i]._children = collected != null ? collected.ToImmutableArray() : ImmutableArray<ActiveCulture>.Empty;
        }
        _index = index;
        _all = all.ToImmutableArray();
    }

    /// <summary>
    /// Gets all the active cultures indexed by their <see cref="ActiveCulture.Index"/> (including the <see cref="Root"/>).
    /// <para>
    /// Note that the order is irrelevant: this is not the same as traversing the <see cref="ActiveCulture.Children"/>
    /// sets in depth or breadth order.
    /// </para>
    /// </summary>
    public ImmutableArray<ActiveCulture> AllActiveCultures => _all;

    /// <summary>
    /// Gets the root <see cref="NormalizedCultureInfo.CodeDefault"/>.
    /// </summary>
    public ActiveCulture Root => _all[0];

    /// <summary>
    /// Gets the number of active cultures (the <see cref="Root"/> is always active).
    /// </summary>
    public int Count => _all.Length;

    /// <summary>
    /// Gets whether a culture belongs to this set.
    /// </summary>
    /// <param name="c">The culture.</param>
    /// <returns>True if this culture belongs to this active set. False otherwise.</returns>
    public bool Contains( NormalizedCultureInfo c ) => _index.ContainsKey( c );

    /// <summary>
    /// Gets an <see cref="ActiveCulture"/> if the culture belongs to this set.
    /// </summary>
    /// <param name="c">The culture.</param>
    /// <returns>The active culture or null.</returns>
    public ActiveCulture? Get( NormalizedCultureInfo c ) => _index.GetValueOrDefault( c );

    /// <summary>
    /// Gets an <see cref="ActiveCulture"/> if the culture belongs to this set.
    /// </summary>
    /// <param name="c">The culture.</param>
    /// <param name="aC">The resulting active culture.</param>
    /// <returns>True if the culture is an active one. False otherwise.</returns>
    public bool TryGet( NormalizedCultureInfo c, [NotNullWhen(true)]out ActiveCulture? aC ) => _index.TryGetValue( c, out aC );

    ActiveCulture Register( Dictionary<NormalizedCultureInfo, ActiveCulture> index,
                            List<ActiveCulture> all,
                            ImmutableArray<ActiveCulture> root,
                            NormalizedCultureInfo c )
    {
        ActiveCulture? newOne;
        if( c.Fallbacks.Length == 0 )
        {
            newOne = new ActiveCulture( this, c, index.Count, root[0], root );
        }
        else
        {
            NormalizedCultureInfo pKey = c.Fallbacks[0];
            if( !index.TryGetValue( pKey, out var parent ) )
            {
                parent = Register( index, all, root, pKey );
            }
            newOne = new ActiveCulture( this, c, index.Count, parent, parent.Path.Add( parent ) );
        }
        index.Add( c, newOne );
        all.Add( newOne );
        return newOne;
    }

    /// <summary>
    /// Overridden to return the comma separated culture names.
    /// </summary>
    /// <returns>The active culture names.</returns>
    public override string ToString() => _all.Select( c => c.ToString() ).Concatenate();
}
