using CK.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Base class for all <see cref="EngineAttribute"/> implementations.
/// <para>
/// This is the non generic base class: there are no constraints on <see cref="Attribute"/>, <see cref="ParentImpl"/>
/// and <see cref="ChildrenImpl"/> types nor on the <see cref="DecoratedItem"/> (that can be any kind of
/// <see cref="CachedMember"/> or a <see cref="ICachedType"/>).
/// </para>
/// </summary>
public abstract class EngineAttributeImpl : IEngineAttributeImpl
{
    [AllowNull] private protected ICachedItem _decoratedItem;
    [AllowNull] private protected IEngineAttribute _attribute;
    EngineAttributeImpl? _parentImpl;
    [AllowNull] IReadOnlyCollection<EngineAttributeImpl> _children;
    EngineAttributeImpl? _nextChildImpl;
    internal ImmutableArray<object> _itemAttributes;

    /// <summary>
    /// Gets the decorated item.
    /// </summary>
    public ICachedItem DecoratedItem => _decoratedItem;

    /// <summary>
    /// Gets the original attribute.
    /// </summary>
    public IEngineAttribute Attribute => _attribute;

    /// <summary>
    /// Gets the attribute name without "Attribute" suffix.
    /// </summary>
    public ReadOnlySpan<char> AttributeName => _attribute.GetType().Name.AsSpan( ..^9 );

    /// <summary>
    /// Gets the parent attribute implementation if <see cref="Attribute"/> is a <see cref="IChildEngineAttribute{T}"/>.
    /// </summary>
    public IEngineAttributeImpl? ParentImpl => _parentImpl;

    sealed class ChildrenCollection : IReadOnlyCollection<EngineAttributeImpl>
    {
        EngineAttributeImpl? _firstChildImpl;
        int _count;

        public ChildrenCollection( EngineAttributeImpl first )
        {
            _firstChildImpl = first;
            _count = 1;
        }

        public void Add( EngineAttributeImpl c )
        {
            _count++;
            if( _firstChildImpl == null )
            {
                _firstChildImpl = c;
            }
            else
            {
                // Reverted insertion.
                c._nextChildImpl = _firstChildImpl;
                _firstChildImpl = c;
            }
        }

        public int Count => _count;

        public IEnumerator<EngineAttributeImpl> GetEnumerator()
        {
            var f = _firstChildImpl;
            while( f != null )
            {
                yield return f;
                f = f._nextChildImpl;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Strongly typed collection on <see cref="ChildrenImpl"/>.
        /// <para>
        /// </para>
        /// <see cref="CheckChildType(IActivityMonitor, EngineAttributeImpl, Type, Type)"/> must
        /// be called from <see cref="AddChild(IActivityMonitor, EngineAttributeImpl)"/> otherwise kitten will die.
        /// <para>
        /// This is required because the following is not possible:
        /// <code>
        /// public new IReadOnlyCollection&lt;TChildren&gt; ChildrenImpl => Unsafe.As&lt;IReadOnlyCollection&lt;TChildren&gt;&gt;( base.ChildrenImpl );
        /// </code>
        /// </para>
        /// </summary>
        internal sealed class Adapter<T> : IReadOnlyCollection<T> where T : class, IEngineAttributeImpl
        {
            readonly ChildrenCollection _c;

            public Adapter( ChildrenCollection c )
            {
                Throw.DebugAssert( c.Count > 0 && c.All( x => x is T ) );
                _c = c;
            }

            public int Count => _c.Count;

            public IEnumerator<T> GetEnumerator()
            {
                var f = _c._firstChildImpl;
                while( f != null )
                {
                    yield return Unsafe.As<T>( f );
                    f = f._nextChildImpl;
                }
            }

            IEnumerator IEnumerable.GetEnumerator() => _c.GetEnumerator();
        }

    }

    /// <summary>
    /// Gets the children attribute implementations.
    /// </summary>
    public IReadOnlyCollection<EngineAttributeImpl> ChildrenImpl => _children;

    /// <summary>
    /// Gets all the attributes of the <see cref="DecoratedItem"/> (including this one).
    /// <para>
    /// This is the result of the <see cref="ICachedItem.TryGetAllAttributes(IActivityMonitor, out ImmutableArray{object})"/>
    /// and is available (in advance) when <see cref="OnInitialized(IActivityMonitor)"/> is called.
    /// </para>
    /// </summary>
    public ImmutableArray<object> AllDecoratedAttributes => _itemAttributes;

    internal virtual bool SetFields( IActivityMonitor monitor,
                                     ICachedItem item,
                                     IEngineAttribute attribute,
                                     EngineAttributeImpl? parentImpl )
    {
        _children = ImmutableArray<EngineAttributeImpl>.Empty;
        _decoratedItem = item;
        _attribute = attribute;
        _parentImpl = parentImpl;
        return true; 
    }

    internal bool AddChild( IActivityMonitor monitor, EngineAttributeImpl c )
    {
        if( _children is not ChildrenCollection children )
        {
            _children = new ChildrenCollection( c );
        }
        else
        {
            children.Add( c );
        }
        return OnAddChild( monitor, c );
    }

    internal virtual bool OnAddChild( IActivityMonitor monitor, EngineAttributeImpl c ) => true;

    /// <summary>
    /// Extension point called once all attributes implementation on the <see cref="DecoratedItem"/>
    /// have been successfully initialized (<see cref="AllDecoratedAttributes"/> is available).
    /// <para>
    /// Default implementation does nothing and always returns true.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True on success, false on error.</returns>
    internal protected virtual bool OnInitialized( IActivityMonitor monitor ) => true;

    /// <summary>
    /// Helper that checks an <see cref="Attribute"/> type.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="impl">The current implementation (this).</param>
    /// <param name="expectedAttrType">The expected attribute type.</param>
    /// <param name="attrType">The actual attribute type.</param>
    /// <returns>True on success, false on error.</returns>
    internal static bool CheckAttributeType( IActivityMonitor monitor,
                                             EngineAttributeImpl impl,
                                             Type expectedAttrType,
                                             Type attrType )
    {
        if( !expectedAttrType.IsAssignableFrom( attrType ) )
        {
            monitor.Error( $"Attribute implementation '{impl.GetType():N}' expects attribute type '{expectedAttrType:N}'. Got a '{attrType:N}' that is not a '{expectedAttrType.Name}'." );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Helper that checks a required <see cref="ParentImpl"/> type (a null <paramref name="parentImpl"/>
    /// logs an error and returnd false).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="impl">The current implementation (this).</param>
    /// <param name="expectedParentType">The expected parent type.</param>
    /// <param name="parentImpl">The parent implementation. Can be null (and this is an error).</param>
    /// <returns>True on success, false on error.</returns>
    internal static bool CheckParentType( IActivityMonitor monitor,
                                          EngineAttributeImpl impl,
                                          Type expectedParentType,
                                          [NotNullWhen(true)]EngineAttributeImpl? parentImpl )
    {
        Type? actualParentType = parentImpl?.GetType();
        if( !expectedParentType.IsAssignableFrom( actualParentType ) )
        {
            monitor.Error( $"Attribute implementation '{impl.GetType():N}' expects a parent of type '{expectedParentType:N}' but got '{actualParentType:N}' that is not a '{expectedParentType.Name}'." );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Helper that checks a child's type.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="impl">The current implementation (this).</param>
    /// <param name="expectedChildType">The expected child type.</param>
    /// <param name="actualChildType">The child's type to validate.</param>
    /// <returns>True on success, false on error.</returns>
    internal static bool CheckChildType( IActivityMonitor monitor,
                                         EngineAttributeImpl impl,
                                         Type expectedChildType,
                                         Type actualChildType )
    {
        if( !expectedChildType.IsAssignableFrom( actualChildType ) )
        {
            monitor.Error( $"Attribute implementation '{impl.GetType():N}' expects a child of type '{expectedChildType:N}' but got '{actualChildType:N}' that is not a '{expectedChildType.Name}'." );
            return false;
        }
        return true;
    }

    internal static IReadOnlyCollection<T> CreateTypedChildrenAdapter<T>( EngineAttributeImpl parent ) where T : class, IEngineAttributeImpl
    {
        return parent._children is ChildrenCollection c
                ? new ChildrenCollection.Adapter<T>( c )
                : ImmutableArray<T>.Empty;
    }

    void IEngineAttributeImpl.LocalImplentationOnly() {}
}
