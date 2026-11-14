using CK.Core;
using System;
using System.Collections.Generic;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Abstraction for <see cref="EngineAttributeImpl"/>.
/// </summary>
public interface IEngineAttributeImpl
{
    /// <summary>
    /// Gets the original attribute.
    /// </summary>
    IEngineAttribute Attribute { get; }

    /// <summary>
    /// Gets the decorated item.
    /// </summary>
    ICachedItem DecoratedItem { get; }

    /// <summary>
    /// Gets the attribute name without "Attribute" suffix.
    /// </summary>
    ReadOnlySpan<char> AttributeName { get; }

    /// <summary>
    /// Gets the parent attribute implementation if <see cref="Attribute"/> is a <see cref="IChildEngineAttribute{T}"/>.
    /// </summary>
    IEngineAttributeImpl? ParentImpl { get; }

    /// <summary>
    /// Gets the children attribute implementations.
    /// </summary>
    IReadOnlyCollection<EngineAttributeImpl> ChildrenImpl { get; }

    internal void LocalImplentationOnly();
}


