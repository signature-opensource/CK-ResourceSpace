using CK.Core;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace CK.Engine.TypeCollector;

public abstract partial class CachedItem
{
    /// <inheritdoc />
    public bool TryGetAllAttributes( IActivityMonitor monitor, out ImmutableArray<object> attributes )
    {
        return _finalAttributesInitialized
                ? (attributes = _finalAttributes).IsDefault is false
                : InitAllAttributes( monitor, this, RawAttributes, ref _finalAttributesInitialized, ref _finalAttributes, out attributes );
    }

    internal static bool InitAllAttributes( IActivityMonitor monitor,
                                            ICachedItem decoratedItem,
                                            ImmutableArray<object> rawAttributes,
                                            ref bool initialized,
                                            ref ImmutableArray<object> cachedAttributes,
                                            out ImmutableArray<object> attributes )
    {
        initialized = true;
        if( rawAttributes.Length == 0 )
        {
            cachedAttributes = attributes = rawAttributes;
            return true;
        }
        attributes = default;
        for( int i = 0; i < rawAttributes.Length; i++ )
        {
            var attr = rawAttributes[i];
            if( attr is EngineAttribute )
            {
                if( !DoInitAllAttributes( monitor, decoratedItem, rawAttributes, i, out attributes ) )
                {
                    return false;
                }
                break;
            }
        }
        cachedAttributes = attributes;
        return true;

        static bool DoInitAllAttributes( IActivityMonitor monitor,
                                         ICachedItem decoratedItem,
                                         ImmutableArray<object> rawAttributes,
                                         int firstAttrIndex,
                                         out ImmutableArray<object> attributes )
        {
            bool success = true;
            attributes = default;
            var result = rawAttributes.ToArray();
            bool parentBindingSuccess = true;
            for( int i = firstAttrIndex; i < rawAttributes.Length; i++ )
            {
                ref object attr = ref result[i];
                if( attr is EngineAttribute engineAttr )
                {
                    var impl = Instantiate( monitor,
                                            decoratedItem,
                                            engineAttr );
                    // On instantiation error, we don't try to detect further errors.
                    if( impl == null )
                    {
                        return false;
                    }
                    // Replaces the attribute by its implementation in result array
                    // and if it is a EngineAttribute<T>, tries to locate an
                    // assignable parent among the previous attribute implementations.
                    // If we can't find the parent, parentBindingSuccess becomes false
                    // and we'll log a detailed error.
                    attr = impl;
                    EngineAttributeImpl? parentImpl = null;
                    var parentType = engineAttr.ParentEngineAttributeType;
                    if( parentType != null )
                    {
                        // If the "Attribute" suffix is not here, this is an instantiation error.
                        if( !CheckAttributeSuffix( monitor, parentType ) )
                        {
                            return false;
                        }
                        for( int j = firstAttrIndex; j < i; j++ )
                        {
                            if( result[j] is EngineAttributeImpl candidate
                                && parentType.IsAssignableFrom( candidate.Attribute.GetType() ) )
                            {
                                parentImpl = candidate;
                                break;
                            }
                        }
                    }
                    // Always initialize the fields (needed to log the
                    // error on parent binding error).
                    bool bindingSuccess = parentType == null || parentImpl != null;
                    success &= impl.SetFields( monitor, decoratedItem, engineAttr, parentImpl )
                               && bindingSuccess;
                    parentBindingSuccess &= bindingSuccess;
                }
            }
            // If we miss a parent, we log the details and give up.
            if( !parentBindingSuccess )
            {
                LogParentBindingError( monitor, decoratedItem, firstAttrIndex, result );
                return false;
            }
            if( success )
            {
                // No parent binding error. To add the children to their parents we
                // go bottom up so that the linked list keeps the children order.
                for( int i = result.Length - 1; i >= firstAttrIndex; i-- )
                {
                    if( result[i] is EngineAttributeImpl attr && attr.ParentImpl != null )
                    {
                        Throw.DebugAssert( attr.ParentImpl is EngineAttributeImpl );
                        success &= Unsafe.As<EngineAttributeImpl>( attr.ParentImpl ).AddChild( monitor, attr );
                    }
                }
                if( success )
                {
                    // On success (parent/child relationships are fine), we set the result immutable
                    // array and call OnInitialized().
                    attributes = ImmutableCollectionsMarshal.AsImmutableArray( result );
                    success &= InitializeAttributes( monitor, decoratedItem, firstAttrIndex, attributes );
                }
            }
            // Finally: build the result and return true or log an error (to secure false success
            // without error log) and return false.
            if( success )
            {
            }
            else
            {
                monitor.Fatal( $"Failed to initialize Attributes on {decoratedItem}." );
            }
            return success;

            static bool CheckAttributeSuffix( IActivityMonitor monitor, Type tAttr )
            {
                var tName = tAttr.Name.AsSpan();
                if( tName.Length <= 9 || !tName[^9..].SequenceEqual( "Attribute" ) )
                {
                    monitor.Error( $"""
                            Attribute name '{tAttr:N}' is invalid: it must be suffixed by "Attribute".
                            """ );
                    return false;
                }
                return true;
            }

            static EngineAttributeImpl? Instantiate( IActivityMonitor monitor,
                                                     ICachedItem decoratedItem,
                                                     EngineAttribute attr )
            {
                var aqn = attr.ActualAttributeTypeAssemblyQualifiedName;
                if( !CheckAttributeSuffix( monitor, attr.GetType() ) )
                {
                    return null;
                }
                try
                {
                    var type = Type.GetType( aqn, throwOnError: true, ignoreCase: false );
                    if( !typeof( EngineAttributeImpl ).IsAssignableFrom( type ) )
                    {
                        monitor.Fatal( $"""
                                Invalid engine type resolution: '{aqn}' for:
                                [{attr.GetType().Name.AsSpan( ..^9 )}]
                                {decoratedItem}
                                The Attribute implementation type must specialize 'EngineAttributeImpl'.
                                """ );
                        return null;
                    }
                    return (EngineAttributeImpl)Activator.CreateInstance( type )!;
                }
                catch( Exception ex )
                {
                    monitor.Fatal( $"""
                            While creating attribute implementation '{aqn}' for:
                            [{attr.GetType().Name.AsSpan( ..^9 )}]
                            {decoratedItem}
                            """, ex );
                    return null;
                }
            }

            static bool InitializeAttributes( IActivityMonitor monitor,
                                              ICachedItem decoratedItem,
                                              int firstAttrIndex,
                                              ImmutableArray<object> result )
            {
                bool success = true;
                for( int i = firstAttrIndex; i < result.Length; i++ )
                {
                    if( result[i] is EngineAttributeImpl attr )
                    {
                        attr._itemAttributes = result;
                    }
                }
                if( success )
                {
                    for( int i = firstAttrIndex; i < result.Length; i++ )
                    {
                        if( result[i] is EngineAttributeImpl attr )
                        {
                            try
                            {
                                success &= attr.OnInitialized( monitor );
                            }
                            catch( Exception ex )
                            {
                                monitor.Error( $"While calling '{attr:N}.OnInitialized' from '{decoratedItem}'.", ex );
                                success = false;
                            }
                        }
                    }
                }
                return success;
            }

            static void LogParentBindingError( IActivityMonitor monitor,
                                               ICachedItem decoratedItem,
                                               int firstAttrIndex,
                                               object[] result )
            {
                using( monitor.OpenError( "Unable to initialize Engine attributes:" ) )
                {
                    StringBuilder b = new StringBuilder();
                    Type? lastMissingParentType = null;
                    int engineNumber = 0;
                    for( int i = firstAttrIndex; i < result.Length; i++ )
                    {
                        if( result[i] is EngineAttributeImpl attr )
                        {
                            ++engineNumber;
                            var parentType = attr.Attribute.ParentEngineAttributeType;
                            if( parentType != null && attr.ParentImpl == null )
                            {
                                WriteBindingError( b, attr, parentType, result, ref lastMissingParentType );
                            }
                            else
                            {
                                b.Append( '[' ).Append( attr.AttributeName ).Append( "] (n°" ).Append( engineNumber ).Append( ')' );
                                lastMissingParentType = null;
                            }
                        }
                        b.AppendLine();
                    }
                    decoratedItem.Write( b );
                    monitor.Trace( b.ToString() );
                }

                static void WriteBindingError( StringBuilder b,
                                               EngineAttributeImpl impl,
                                               Type parentType,
                                               object[] attributes,
                                               ref Type? lastExpectedPrimaryType )
                {
                    b.Append( "**[" ).Append( impl.AttributeName ).Append( "]**" );
                    if( lastExpectedPrimaryType == parentType )
                    {
                        b.Append( " (Same as above.)" );
                    }
                    else
                    {
                        var possible = attributes.OfType<EngineAttributeImpl>().Select( ( p, idx ) => (p, idx) )
                                             .Where( v => parentType.IsAssignableFrom( v.p.Attribute.GetType() ) );
                        int count = 0;
                        foreach( var v in possible )
                        {
                            if( ++count > 1 ) b.Append( " or " );
                            else b.Append( " Must be after " );
                            b.Append( '[' ).Append( v.p.AttributeName ).Append( "] (n°" ).Append( v.idx + 1 ).Append( ')' );
                        }
                        if( count == 0 )
                        {
                            b.Append( " Missing a [" )
                             .Append( parentType.Name.AsSpan( ..^9 ) )
                             .Append( "] attribute (or a specialization) above this one" );
                        }
                        b.Append( '.' );
                        lastExpectedPrimaryType = parentType;
                    }
                }
            }
        }


    }

}
