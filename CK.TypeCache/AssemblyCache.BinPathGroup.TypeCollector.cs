using CK.Core;
using CK.Setup;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System;

namespace CK.TypeCache;


public sealed partial class AssemblyCache // BinPathGroup.TypeCollector
{
    public sealed partial class BinPathGroup
    {
        internal bool FinalizeAndCollectTypes( IActivityMonitor monitor, GlobalTypeCache typeCache )
        {
            // Builds the GroupName.
            if( _configurations.Count > 1 )
            {
                _configurations.Sort( ( a, b ) => a.Name.CompareTo( b.Name ) );
                _groupName = _configurations.Select( b => b.Name ).Concatenate( "-" );
            }
            monitor.Trace( $"Skipped {_systemSkipped.Count} system assemblies: {_systemSkipped.Select( a => a.Name ).Concatenate()}." );
            // Useless to keep the list content.
            _systemSkipped.Clear();
            if( !_success )
            {
                monitor.Trace( $"BinPathGroup '{_groupName}' is on error." );
                return false;
            }
            using var _ = monitor.OpenInfo( $"Collecting types for BinPathGroup '{_groupName}' from head models: '{_heads.Keys.Select( a => a.Name ).Concatenate( "', '" )}'." );
            using var hasher = IncrementalHash.CreateHash( HashAlgorithmName.SHA1 );
            hasher.Append( _path.Path );

            var c = new ConfiguredTypeSet();
            bool success = true;
            foreach( var head in _heads.Keys )
            {
                head.AddHash( hasher );
                success &= CollectTypes( monitor, typeCache, head, out var headC );
                c.Add( monitor, headC, head.ToString() );
            }
            _signature = new SHA1Value( hasher, resetHasher: false );
            if( success ) _result = c;
            return success;

            static bool CollectTypes( IActivityMonitor monitor, GlobalTypeCache typeCache, CachedAssembly assembly, out ConfiguredTypeSet c )
            {
                Throw.DebugAssert( assembly.IsInitialAssembly && !assembly.Kind.IsSkipped() );
                if( assembly._types != null )
                {
                    c = assembly._types;
                    return true;
                }
                c = new ConfiguredTypeSet();
                var assemblySourceName = assembly.ToString();
                using var _ = monitor.OpenInfo( assemblySourceName );
                bool success = true;
                foreach( var sub in assembly.Models )
                {
                    success &= CollectTypes( monitor, typeCache, sub, out var subC );
                    c.Add( monitor, subC, assemblySourceName );
                }
                // Type selection for this assembly.
                // Consider the visible classes, interfaces, value types and enums excluding any generic type definitions.
                // These are the only kind of types that we need to start a CKomposable setup.
                c.Add( assembly.Assembly.GetExportedTypes()
                                             .Where( t => (t.IsClass || t.IsInterface || t.IsValueType || t.IsEnum) && !t.IsGenericTypeDefinition )
                                             .Select( typeCache.Get ) );
                // Don't merge the 2 loops here!
                // We must first handle the Add and then the Remove.
                // 1 - Add types.
                List<ICachedType>? changed = null;
                foreach( var a in assembly.CustomAttributes )
                {
                    if( a.AttributeType == typeof( AddTypeAttribute ) )
                    {
                        var ctorArgs = a.ConstructorArguments;
                        // Constructor (Type, Type[] others):
                        if( ctorArgs[1].Value is Type?[] others )
                        {
                            // Filters out null thanks to "is".
                            if( ctorArgs[0].Value is Type t )
                            {
                                success &= HandleTypeConfiguration( monitor, typeCache, c, ref changed, add: true, assemblySourceName, t, ConfigurableAutoServiceKind.None );
                            }
                            // Maximal precautions: filters out any potential null.
                            foreach( var o in others )
                            {
                                if( o == null ) continue;
                                success &= HandleTypeConfiguration( monitor, typeCache, c, ref changed, add: true, assemblySourceName, o, ConfigurableAutoServiceKind.None );
                            }
                        }
                        else if( ctorArgs[1].Value is ConfigurableAutoServiceKind kind )
                        {
                            // Filters out null thanks to "is".
                            if( ctorArgs[0].Value is Type t )
                            {
                                success &= HandleTypeConfiguration( monitor, typeCache, c, ref changed, add: true, assemblySourceName, t, kind );
                            }
                        }
                    }
                }
                if( success && changed != null )
                {
                    monitor.Info( $"Assembly '{assembly.Name}' explicitly adds {changed.Count} types: '{changed.Select( t => t.CSharpName ).Concatenate( "', '" )}'." );
                    changed.Clear();
                }
                // 2 - Remove types.
                foreach( var a in assembly.CustomAttributes )
                {
                    if( a.AttributeType == typeof( CK.Setup.RemoveTypeAttribute ) )
                    {
                        var ctorArgs = a.ConstructorArguments;
                        if( ctorArgs[0].Value is Type t )
                        {
                            success &= HandleTypeConfiguration( monitor,
                                                                typeCache,
                                                                c,
                                                                ref changed,
                                                                add: false,
                                                                assemblySourceName,
                                                                t,
                                                                ConfigurableAutoServiceKind.None );
                        }
                        if( ctorArgs[1].Value is Type?[] others && others.Length > 0 )
                        {
                            foreach( var o in others )
                            {
                                if( o == null ) continue;
                                success &= HandleTypeConfiguration( monitor,
                                                                    typeCache,
                                                                    c,
                                                                    ref changed,
                                                                    add: false,
                                                                    assemblySourceName,
                                                                    o,
                                                                    ConfigurableAutoServiceKind.None );
                            }
                        }
                    }
                }
                if( success && changed != null )
                {
                    monitor.Info( $"Assembly '{assembly.Name}' explicitly removed {changed.Count} types from registration: '{changed.Select( t => t.CSharpName ).Concatenate( "', '" )}'." );
                }
                monitor.CloseGroup( $"{c.AllTypes.Count} types." );
                assembly._types = c;
                return success;

                static bool HandleTypeConfiguration( IActivityMonitor monitor,
                                                     GlobalTypeCache typeCache,
                                                     ConfiguredTypeSet c,
                                                     ref List<ICachedType>? changed,
                                                     bool add,
                                                     string sourceAssemblyName,
                                                     Type t,
                                                     ConfigurableAutoServiceKind kind )
                {
                    var cT = typeCache.Get( t );
                    var error = GetConfiguredTypeErrorMessage( typeCache, cT, kind );
                    if( error != null )
                    {
                        monitor.Error( $"Invalid [assembly:{(add ? "Add" : "Remove")}TypeAttribute] in {sourceAssemblyName}: type '{t:N}' {error}." );
                        return false;
                    }
                    if( add ? c.Add( monitor, sourceAssemblyName, cT, kind ) : c.Remove( cT ) )
                    {
                        changed ??= new List<ICachedType>();
                        changed.Add( cT );
                    }
                    return true;
                }
            }
        }

        /// <summary>
        /// Checks that a type can be configured with a kind.
        /// </summary>
        /// <param name="typeCache">The type cache.</param>
        /// <param name="type">The configured type.</param>
        /// <param name="kind">Its configured kind.</param>
        /// <returns>Null if it can be, otherwise the end of an error message.</returns>
        public static string? GetConfiguredTypeErrorMessage( GlobalTypeCache typeCache, ICachedType type, ConfigurableAutoServiceKind kind )
        {
            if( type.EngineUnhandledType != EngineUnhandledType.None )
            {
                return type.EngineUnhandledType switch
                {
                    EngineUnhandledType.NullFullName => "has a null FullName",
                    EngineUnhandledType.FromDynamicAssembly => "is defined by a dynamic assembly",
                    EngineUnhandledType.NotVisible => "must be public (visible outside of its asssembly)",
                    EngineUnhandledType.NotClassEnumValueTypeOrEnum => "must be an enum, a value type, a class or an interface",
                    _ => Throw.NotSupportedException<string>( type.EngineUnhandledType.ToString() )
                };
            }
            if( kind != ConfigurableAutoServiceKind.None )
            {
                string? k = null;
                if( type.Interfaces.Contains( typeCache.KnownTypes.IAutoService ) )
                {
                    k = nameof( IAutoService );
                }
                else if( type.Interfaces.Contains( typeCache.KnownTypes.IRealObject ) )
                {
                    k = nameof( IRealObject );
                }
                else if( type.Interfaces.Contains( typeCache.KnownTypes.IPoco ) )
                {
                    k = nameof( IPoco );
                }
                if( k != null )
                {
                    return $"is a {k}. IAutoService, IRealObject and IPoco cannot be externally configured";
                }
            }
            return null;
        }

    }

}
