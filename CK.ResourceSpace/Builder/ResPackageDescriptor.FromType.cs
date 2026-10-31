using System.Collections.Generic;
using System.Linq;
using CK.Engine.TypeCollector;

namespace CK.Core;

public sealed partial class ResPackageDescriptor
{
    internal bool InitializeFromType( IActivityMonitor monitor, bool? isOptional )
    {
        Throw.DebugAssert( _type != null );
        _isGroup = !_type.Interfaces.Contains( _context.ResourcePackageType );
        _isOptional = isOptional ?? !IsRequired( _type );
        // Detect a useless CKPackage.xml for the type: currently, there's
        // no "merge" possible: the type drives.
        var descriptor = _resources.GetResource( "CKPackage.xml" );
        if( descriptor.IsValid )
        {
            monitor.Warn( $"Found {descriptor} for type '{_type:N}'. Ignored." );
        }
        var visitor = new RelationCollector( this );
        return ResPackageRelationReader.Read( monitor, _type, ref visitor );

        static bool IsRequired( ICachedType type )
        {
            return type.RawAttributes.OfType<IOptionalResourceGroupAttribute>()
                                     .All( a => !a.IsOptional );
        }
    }

    // Fills the relation lists of the descriptor (the lists are created on demand).
    readonly struct RelationCollector : IResPackageRelationVisitor
    {
        readonly ResPackageDescriptor _d;

        public RelationCollector( ResPackageDescriptor d ) => _d = d;

        public void OnPackage( Ref package ) => _d._package = package;

        public void OnRelation( ResRelationKind kind, Ref target )
        {
            switch( kind )
            {
                case ResRelationKind.Requires: Add( ref _d._requires, target ); break;
                case ResRelationKind.RequiredBy: Add( ref _d._requiredBy, target ); break;
                case ResRelationKind.Children: Add( ref _d._children, target ); break;
                default:
                    Throw.DebugAssert( kind == ResRelationKind.Groups );
                    Add( ref _d._groups, target );
                    break;
            }

            // The same target can be declared more than once (by an attribute and a IResourceGroupTopologyAttribute):
            // a required reference wins over an optional one.
            static void Add( ref List<Ref>? list, Ref target )
            {
                if( list == null )
                {
                    list = new List<Ref> { target };
                    return;
                }
                for( int i = 0; i < list.Count; i++ )
                {
                    var r = list[i];
                    if( Equals( r._ref, target._ref ) )
                    {
                        if( r.IsOptional && !target.IsOptional ) list[i] = target;
                        return;
                    }
                }
                list.Add( target );
            }
        }
    }

}
