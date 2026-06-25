using CK.Engine.TypeCollector;
using System.Collections.Generic;

namespace CK.Core;

/// <summary>
/// Provides extensions on <see cref="ResPackageDescriptor"/> related types.
/// </summary>
public static class ResPackageDescriptorExtensions
{
    /// <summary>
    /// Adds a <see cref="ICachedType"/>, possibly optional to this list.
    /// </summary>
    /// <param name="refs">This list of references.</param>
    /// <param name="type">The cached type to add to the list.</param>
    /// <param name="optional">True for an optional reference.</param>
    public static void Add( this IList<ResPackageDescriptor.Ref> refs, ICachedType type, bool optional = false )
    {
        refs.Add( new ResPackageDescriptor.Ref( type, optional ) );
    }
}
