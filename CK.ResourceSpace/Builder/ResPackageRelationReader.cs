using CK.Engine.TypeCollector;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace CK.Core;

using Ref = ResPackageDescriptor.Ref;

/// <summary>
/// The relations of a package (the package's <see cref="ResPackageDescriptor.Package"/> is handled apart).
/// </summary>
enum ResRelationKind
{
    Requires,
    RequiredBy,
    Children,
    Groups
}

/// <summary>
/// Receives the relations read by <see cref="ResPackageRelationReader.Read{TVisitor}(IActivityMonitor, ICachedType, ref TVisitor)"/>.
/// </summary>
interface IResPackageRelationVisitor
{
    /// <summary>
    /// Called once (after the relations) if the type declares its package.
    /// </summary>
    /// <param name="package">The package reference.</param>
    void OnPackage( Ref package );

    /// <summary>
    /// Called for each relation. <see cref="Ref.IsOptional"/> is true for the [Optional...] variants.
    /// </summary>
    /// <param name="kind">The relation kind.</param>
    /// <param name="target">The related package reference.</param>
    void OnRelation( ResRelationKind kind, Ref target );
}

/// <summary>
/// Reads the topology relations declared by the attributes of a type: the name based and generic [Requires], [RequiredBy],
/// [Children], [Groups] and [Package] attributes, their [Optional...] variants and the <see cref="IResourceGroupTopologyAttribute"/>.
/// <para>
/// No list is allocated here: the visitor decides what to do with each relation.
/// </para>
/// </summary>
static class ResPackageRelationReader
{
    // Maps the generic attribute definitions (all arities) to their relation.
    static readonly Dictionary<Type, (ResRelationKind Kind, bool Optional)> _genericRelations = CreateGenericRelations();

    static Dictionary<Type, (ResRelationKind, bool)> CreateGenericRelations()
    {
        var d = new Dictionary<Type, (ResRelationKind, bool)>();
        Add( d, ResRelationKind.Requires, false,
             typeof( RequiresAttribute<> ), typeof( RequiresAttribute<,> ), typeof( RequiresAttribute<,,> ),
             typeof( RequiresAttribute<,,,> ), typeof( RequiresAttribute<,,,,> ), typeof( RequiresAttribute<,,,,,> ) );
        Add( d, ResRelationKind.Requires, true,
             typeof( OptionalRequiresAttribute<> ), typeof( OptionalRequiresAttribute<,> ), typeof( OptionalRequiresAttribute<,,> ),
             typeof( OptionalRequiresAttribute<,,,> ), typeof( OptionalRequiresAttribute<,,,,> ), typeof( OptionalRequiresAttribute<,,,,,> ) );
        Add( d, ResRelationKind.RequiredBy, false,
             typeof( RequiredByAttribute<> ), typeof( RequiredByAttribute<,> ), typeof( RequiredByAttribute<,,> ),
             typeof( RequiredByAttribute<,,,> ), typeof( RequiredByAttribute<,,,,> ), typeof( RequiredByAttribute<,,,,,> ) );
        Add( d, ResRelationKind.RequiredBy, true,
             typeof( OptionalRequiredByAttribute<> ), typeof( OptionalRequiredByAttribute<,> ), typeof( OptionalRequiredByAttribute<,,> ),
             typeof( OptionalRequiredByAttribute<,,,> ), typeof( OptionalRequiredByAttribute<,,,,> ), typeof( OptionalRequiredByAttribute<,,,,,> ) );
        Add( d, ResRelationKind.Groups, false,
             typeof( GroupsAttribute<> ), typeof( GroupsAttribute<,> ), typeof( GroupsAttribute<,,> ),
             typeof( GroupsAttribute<,,,> ), typeof( GroupsAttribute<,,,,> ), typeof( GroupsAttribute<,,,,,> ) );
        Add( d, ResRelationKind.Groups, true,
             typeof( OptionalGroupsAttribute<> ), typeof( OptionalGroupsAttribute<,> ), typeof( OptionalGroupsAttribute<,,> ),
             typeof( OptionalGroupsAttribute<,,,> ), typeof( OptionalGroupsAttribute<,,,,> ), typeof( OptionalGroupsAttribute<,,,,,> ) );
        Add( d, ResRelationKind.Children, false,
             typeof( ChildrenAttribute<> ), typeof( ChildrenAttribute<,> ), typeof( ChildrenAttribute<,,> ),
             typeof( ChildrenAttribute<,,,> ), typeof( ChildrenAttribute<,,,,> ), typeof( ChildrenAttribute<,,,,,> ) );
        Add( d, ResRelationKind.Children, true,
             typeof( OptionalChildrenAttribute<> ), typeof( OptionalChildrenAttribute<,> ), typeof( OptionalChildrenAttribute<,,> ),
             typeof( OptionalChildrenAttribute<,,,> ), typeof( OptionalChildrenAttribute<,,,,> ), typeof( OptionalChildrenAttribute<,,,,,> ) );
        return d;

        static void Add( Dictionary<Type, (ResRelationKind, bool)> d, ResRelationKind kind, bool optional, params Type[] types )
        {
            foreach( var t in types ) d.Add( t, (kind, optional) );
        }
    }

    /// <summary>
    /// Reads the relations of a type. At most one package can be declared.
    /// </summary>
    /// <typeparam name="TVisitor">The visitor type.</typeparam>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="type">The type to read.</param>
    /// <param name="visitor">The visitor.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool Read<TVisitor>( IActivityMonitor monitor, ICachedType type, ref TVisitor visitor )
        where TVisitor : struct, IResPackageRelationVisitor
    {
        bool success = true;
        Ref package = default;
        int packageCount = 0;
        foreach( var a in type.AttributesData )
        {
            Type aType = a.AttributeType;
            if( aType == typeof( RequiresAttribute ) ) HandleMultiName( a, ResRelationKind.Requires, false, ref visitor );
            else if( aType == typeof( OptionalRequiresAttribute ) ) HandleMultiName( a, ResRelationKind.Requires, true, ref visitor );
            else if( aType == typeof( RequiredByAttribute ) ) HandleMultiName( a, ResRelationKind.RequiredBy, false, ref visitor );
            else if( aType == typeof( OptionalRequiredByAttribute ) ) HandleMultiName( a, ResRelationKind.RequiredBy, true, ref visitor );
            else if( aType == typeof( GroupsAttribute ) ) HandleMultiName( a, ResRelationKind.Groups, false, ref visitor );
            else if( aType == typeof( OptionalGroupsAttribute ) ) HandleMultiName( a, ResRelationKind.Groups, true, ref visitor );
            else if( aType == typeof( ChildrenAttribute ) ) HandleMultiName( a, ResRelationKind.Children, false, ref visitor );
            else if( aType == typeof( OptionalChildrenAttribute ) ) HandleMultiName( a, ResRelationKind.Children, true, ref visitor );
            else if( aType == typeof( PackageAttribute ) )
            {
                ++packageCount;
                package = (string)a.ConstructorArguments[0].Value!;
            }
            else if( aType.IsGenericType )
            {
                var gType = aType.GetGenericTypeDefinition();
                if( gType == typeof( PackageAttribute<> ) )
                {
                    ++packageCount;
                    package = aType.GetGenericArguments()[0];
                }
                else if( _genericRelations.TryGetValue( gType, out var relation ) )
                {
                    foreach( var t in aType.GetGenericArguments() )
                    {
                        visitor.OnRelation( relation.Kind, new Ref( t, relation.Optional ) );
                    }
                }
            }
        }
        foreach( var a in type.RawAttributes )
        {
            if( a is IResourceGroupTopologyAttribute topology )
            {
                success &= HandleTypes( monitor, type, a, "Requires", topology.Requires, ResRelationKind.Requires, false, ref visitor );
                success &= HandleTypes( monitor, type, a, "OptionalRequires", topology.OptionalRequires, ResRelationKind.Requires, true, ref visitor );
                success &= HandleTypes( monitor, type, a, "RequiredBy", topology.RequiredBy, ResRelationKind.RequiredBy, false, ref visitor );
                success &= HandleTypes( monitor, type, a, "OptionalRequiredBy", topology.OptionalRequiredBy, ResRelationKind.RequiredBy, true, ref visitor );
                success &= HandleTypes( monitor, type, a, "Children", topology.Children, ResRelationKind.Children, false, ref visitor );
                success &= HandleTypes( monitor, type, a, "OptionalChildren", topology.OptionalChildren, ResRelationKind.Children, true, ref visitor );
                success &= HandleTypes( monitor, type, a, "Groups", topology.Groups, ResRelationKind.Groups, false, ref visitor );
                success &= HandleTypes( monitor, type, a, "OptionalGroups", topology.OptionalGroups, ResRelationKind.Groups, true, ref visitor );
                var p = topology.Package;
                if( p != null )
                {
                    if( !typeof( IResourcePackage ).IsAssignableFrom( p ) )
                    {
                        monitor.Error( $"Invalid Package '{p.ToCSharpName()}' declared by [{a.GetType().ToCSharpName()}] on '{type.CSharpName}': it is not a IResourcePackage." );
                        success = false;
                    }
                    else
                    {
                        ++packageCount;
                        package = p;
                    }
                }
            }
        }
        if( packageCount > 1 )
        {
            monitor.Error( $"At most one package can be declared for type '{type.CSharpName}' (by [Package<T>], [Package( \"FullName\" )] or a IResourceGroupTopologyAttribute)." );
            success = false;
        }
        else if( package.IsValid )
        {
            visitor.OnPackage( package );
        }
        return success;
    }

    static void HandleMultiName<TVisitor>( CustomAttributeData a, ResRelationKind kind, bool optional, ref TVisitor visitor )
        where TVisitor : struct, IResPackageRelationVisitor
    {
        // The params string[] constructor argument is a ReadOnlyCollection<CustomAttributeTypedArgument>.
        var names = (IReadOnlyCollection<CustomAttributeTypedArgument>)a.ConstructorArguments[0].Value!;
        foreach( var n in names )
        {
            foreach( var name in ((string)n.Value!).Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) )
            {
                visitor.OnRelation( kind, new Ref( name, optional ) );
            }
        }
    }

    static bool HandleTypes<TVisitor>( IActivityMonitor monitor,
                                       ICachedType type,
                                       object attribute,
                                       string memberName,
                                       ReadOnlySpan<Type> types,
                                       ResRelationKind kind,
                                       bool optional,
                                       ref TVisitor visitor )
        where TVisitor : struct, IResPackageRelationVisitor
    {
        bool success = true;
        foreach( var t in types )
        {
            if( t == null || !typeof( IResourceGroup ).IsAssignableFrom( t ) )
            {
                monitor.Error( $"Invalid {memberName} '{t?.ToCSharpName()}' declared by [{attribute.GetType().ToCSharpName()}] on '{type.CSharpName}': it is not a IResourceGroup." );
                success = false;
            }
            else
            {
                visitor.OnRelation( kind, new Ref( t, optional ) );
            }
        }
        return success;
    }
}
