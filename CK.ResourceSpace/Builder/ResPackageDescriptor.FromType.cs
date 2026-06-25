using System.Collections.Generic;
using System.Linq;
using System;
using System.Reflection;
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

        bool errorSinglePackage = false;
        foreach( var a in _type.AttributesData )
        {
            Type aType = a.AttributeType;
            if( aType == typeof( RequiresAttribute ) )
            {
                HandleMultiName( a, ref _requires );
            }
            else if( aType == typeof( RequiredByAttribute ) )
            {
                HandleMultiName( a, ref _requiredBy );
            }
            else if( aType == typeof( GroupsAttribute ) )
            {
                HandleMultiName( a, ref _groups );
            }
            else if( aType == typeof( ChildrenAttribute ) )
            {
                HandleMultiName( a, ref _children );
            }
            else if( aType == typeof( PackageAttribute ) )
            {
                errorSinglePackage |= _package.IsValid;
                _package = (string)a.ConstructorArguments[0].Value!;
            }
            else if( aType.IsGenericType )
            {
                var gType = aType.GetGenericTypeDefinition();
                if( gType == typeof( PackageAttribute<> ) )
                {
                    errorSinglePackage |= _package.IsValid;
                    _package = aType.GetGenericArguments()[0];
                }
                else if( gType == typeof( RequiresAttribute<> ) || gType == typeof( RequiresAttribute<,> ) || gType == typeof( RequiresAttribute<,,> )
                         || gType == typeof( RequiresAttribute<,,,> ) || gType == typeof( RequiresAttribute<,,,,> ) || gType == typeof( RequiresAttribute<,,,,,> ) )
                {
                    AddTypes( aType.GetGenericArguments(), false, ref _requires );
                }
                else if( gType == typeof( OptionalRequiresAttribute<> ) || gType == typeof( OptionalRequiresAttribute<,> ) || gType == typeof( OptionalRequiresAttribute<,,> )
                         || gType == typeof( OptionalRequiresAttribute<,,,> ) || gType == typeof( OptionalRequiresAttribute<,,,,> ) || gType == typeof( OptionalRequiresAttribute<,,,,,> ) )
                {
                    AddTypes( aType.GetGenericArguments(), true, ref _requires );
                }
                else if( gType == typeof( RequiredByAttribute<> ) || gType == typeof( RequiredByAttribute<,> ) || gType == typeof( RequiredByAttribute<,,> )
                         || gType == typeof( RequiredByAttribute<,,,> ) || gType == typeof( RequiredByAttribute<,,,,> ) || gType == typeof( RequiredByAttribute<,,,,,> ) )
                {
                    AddTypes( aType.GetGenericArguments(), false, ref _requiredBy );
                }
                else if( gType == typeof( OptionalRequiredByAttribute<> ) || gType == typeof( OptionalRequiredByAttribute<,> ) || gType == typeof( OptionalRequiredByAttribute<,,> )
                         || gType == typeof( OptionalRequiredByAttribute<,,,> ) || gType == typeof( OptionalRequiredByAttribute<,,,,> ) || gType == typeof( OptionalRequiredByAttribute<,,,,,> ) )
                {
                    AddTypes( aType.GetGenericArguments(), true, ref _requiredBy );
                }
                else if( gType == typeof( GroupsAttribute<> ) || gType == typeof( GroupsAttribute<,> ) || gType == typeof( GroupsAttribute<,,> )
                         || gType == typeof( GroupsAttribute<,,,> ) || gType == typeof( GroupsAttribute<,,,,> ) || gType == typeof( GroupsAttribute<,,,,,> ) )
                {
                    AddTypes( aType.GetGenericArguments(), false, ref _groups );
                }
                else if( gType == typeof( OptionalGroupsAttribute<> ) || gType == typeof( OptionalGroupsAttribute<,> ) || gType == typeof( OptionalGroupsAttribute<,,> )
                         || gType == typeof( OptionalGroupsAttribute<,,,> ) || gType == typeof( OptionalGroupsAttribute<,,,,> ) || gType == typeof( OptionalGroupsAttribute<,,,,,> ) )
                {
                    AddTypes( aType.GetGenericArguments(), true, ref _groups );
                }
                else if( gType == typeof( ChildrenAttribute<> ) || gType == typeof( ChildrenAttribute<,> ) || gType == typeof( ChildrenAttribute<,,> )
                         || gType == typeof( ChildrenAttribute<,,,> ) || gType == typeof( ChildrenAttribute<,,,,> ) || gType == typeof( ChildrenAttribute<,,,,,> ) )
                {
                    AddTypes( aType.GetGenericArguments(), false, ref _children );
                }
                else if( gType == typeof( OptionalChildrenAttribute<> ) || gType == typeof( OptionalChildrenAttribute<,> ) || gType == typeof( OptionalChildrenAttribute<,,> )
                         || gType == typeof( OptionalChildrenAttribute<,,,> ) || gType == typeof( OptionalChildrenAttribute<,,,,> ) || gType == typeof( OptionalChildrenAttribute<,,,,,> ) )
                {
                    AddTypes( aType.GetGenericArguments(), true, ref _children );
                }
            }
        }
        if( errorSinglePackage )
        {
            monitor.Error( $"At most one of [Package<Type>] or [Package( \"FullName\" )] can decorate type '{_type:N}'." );
            return false;
        }
        return true;

        static void HandleMultiName( CustomAttributeData a, ref List<Ref>? list )
        {
            var commaSeparatedPackageFullnames = (string[])a.ConstructorArguments[0].Value!;
            foreach( var n in commaSeparatedPackageFullnames )
            {
                foreach( var name in n.Split( ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries ) )
                {
                    list ??= new List<Ref>( commaSeparatedPackageFullnames.Length );
                    list.Add( name );
                }
            }
        }

        static void AddTypes( Type[] genArgs, bool optional, ref List<Ref>? list )
        {
            foreach( var t in genArgs )
            {
                list ??= new List<Ref>( genArgs.Length );
                list.Add( new Ref( t, optional ) );
            }
        }

        static bool IsRequired( ICachedType type )
        {
            return type.RawAttributes.OfType<IOptionalResourceGroupAttribute>()
                                     .All( a => !a.IsOptional );
        }
    }

}
