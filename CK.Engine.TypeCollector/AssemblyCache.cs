using CK.Core;
using CK.Setup;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CK.Engine.TypeCollector;

/// <summary>
/// Collects, analyses and caches Assembly information.
/// </summary>
public sealed partial class AssemblyCache
{
    // Don't be tempted to move this excluder down to each BinPath as
    // this woult break the PFeature sharing accross BinPath (the CachedAssembly
    // would not be able to host the PFeatures, introducing another cache layer in the
    // architecture. Exclusions must be Global.
    readonly Func<string, bool>? _assemblyExcluder;
    readonly NormalizedPath _appContextPath;
    // CachedAssembly are indexed by their Assembly and their simple name.
    readonly Dictionary<object, CachedAssembly> _assemblies;
    // BinPaths are indexed by their BinPathKey.
    readonly Dictionary<GroupKey, BinPathGroup> _binPaths;
    bool _registrationClosed;

    /// <summary>
    /// Initializes a new empty <see cref="AssemblyCache"/>. <see cref="FindOrCreate(Assembly)"/> must
    /// be used to register assemblies.
    /// <para>
    /// This should be rarely used as this doesn't handle types registration: <see cref="Run(IActivityMonitor, EngineConfiguration)"/>
    /// encapsulates the regular use of an assembly cache.
    /// </para>
    /// </summary>
    /// <param name="assemblyExcluder">Optional filter that can exclude an assembly when returning true.</param>
    public AssemblyCache( Func<string, bool>? assemblyExcluder = null )
        : this( assemblyExcluder, noPlannedRun: true )
    {
        _appContextPath = AppContext.BaseDirectory;
        _assemblyExcluder = assemblyExcluder;
        _assemblies = new Dictionary<object, CachedAssembly>();
        _binPaths = new Dictionary<GroupKey, BinPathGroup>();
    }

    AssemblyCache( Func<string, bool>? assemblyExcluder, bool noPlannedRun )
    {
        _appContextPath = AppContext.BaseDirectory;
        _assemblyExcluder = assemblyExcluder;
        _assemblies = new Dictionary<object, CachedAssembly>();
        _binPaths = new Dictionary<GroupKey, BinPathGroup>();
        _registrationClosed = noPlannedRun;
    }

    /// <summary>
    /// Gets the cached assemblies. CachedAssembly are indexed by their Assembly and their simple name.
    /// </summary>
    public IReadOnlyDictionary<object, CachedAssembly> Assemblies => _assemblies;

    /// <summary>
    /// Registers an assembly that has not been discovered by the <see cref="Run(IActivityMonitor, EngineConfiguration)"/>.
    /// <para>
    /// Assemblies registered here are never "initial" ones (<see cref="CachedAssembly.IsInitialAssembly"/> is false if the
    /// assembly was not discovered by the Run method). They can be <see cref="AssemblyKind.Excluded"/> if the configured
    /// assembly excluder says so: we don't care, this blindly collects the assemblies.
    /// </para>
    /// </summary>
    /// <param name="assembly">The assembly to find or register. Must not be <see cref="Assembly.IsDynamic"/>.</param>
    /// <returns>The cached assembly.</returns>
    public CachedAssembly FindOrCreate( Assembly assembly )
    {
        // This can be called only once registrations are closed, after the Run(IActivityMonitor, EngineConfiguration).
        Throw.CheckArgument( assembly is not null && assembly.IsDynamic is false );
        Throw.CheckState( _registrationClosed is true );
        return FindOrCreate( assembly, null, null, out var _ );
    }

    /// <summary>
    /// Encapsulates the work of this <see cref="AssemblyCache"/> on an engine configuration.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="configuration">The configuration to process.</param>
    /// <returns>The result (<see cref="Result.Success"/> can be false).</returns>
    public static Result Run( IActivityMonitor monitor, EngineConfiguration configuration )
    {
        bool success = true;
        var assemblyCache = new AssemblyCache( configuration.ExcludedAssemblies.Contains, noPlannedRun: false );

        var collector = new List<BinPathGroup>();
        foreach( var b in configuration.BinPaths )
        {
            success &= assemblyCache.Register( monitor, b, collector ).Success;
        }
        // Any new CachedAssemblyInfo will not be IsInitialAssembly: when collecting types
        // any referenced types (base types, interfaces, etc.) may register a new assembly
        // but it will not be an "initial" one.
        assemblyCache._registrationClosed = true;
        // We can now compute the CachedAssembly.Types and safely uses ICachedType from it.
        var typeCache = new GlobalTypeCache( assemblyCache );
        using( monitor.OpenInfo( $"Collecting types for {collector.Count} BinPathGroup." ) )
        {
            foreach( var g in collector )
            {
                success &= g.FinalizeAndCollectTypes( monitor, typeCache );
            }
        }
        // Dumps a summary.
        var sb = new StringBuilder( success ? "Successfully analyzed " : "Error while analyzing " );
        sb.Append( assemblyCache._binPaths.Count ).AppendLine( " BinPath configurations:" );
        foreach( var b in assemblyCache._binPaths.Values )
        {
            if( b.Success )
            {
                sb.AppendLine( $"- {b.ConfiguredTypes.AllTypes.Count} types for group '{b.GroupName}'." );
                sb.AppendLine( $"  From head PFeatures: '{b.HeadAssemblies.Select( p => p.Name ).Concatenate( "', '" )}'." );
            }
            else
            {
                sb.AppendLine( $"- Group '{b.GroupName}' failed." );
            }
        }
        monitor.Log( success ? LogLevel.Info : LogLevel.Error, sb.ToString() );
        return new Result( success, assemblyCache, typeCache, assemblyCache._binPaths.Values );
    }

    /// <summary>
    /// Gets the <see cref="AppContext.BaseDirectory"/> from which assemblies are loaded.
    /// </summary>
    NormalizedPath AppContextBaseDirectory => _appContextPath;

    /// <summary>
    /// Registers a <see cref="BinPathAspectConfiguration"/>. Configurations are grouped
    /// into <see cref="BinPathGroup"/> when their assembly related configurations are similar.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="configuration">The configuration to register.</param>
    /// <param name="collector">
    /// The ordered list of BinPathGroup that are created: this enables to keep the configuration order
    /// for the type collection otherwise we'll handle the types in a random order given by the hash of the
    /// GroupKey in the _binPaths dictionary.
    /// </param>
    /// <returns>The BinPathGroup (contains the configuration).</returns>
    BinPathGroup Register( IActivityMonitor monitor, BinPathConfiguration configuration, List<BinPathGroup> collector )
    {
        var k = new GroupKey( configuration );
        if( !_binPaths.TryGetValue( k, out var binPath ) )
        {
            using( monitor.OpenInfo( $"Analyzing configuration '{configuration.Name}' Path '{configuration.Path}'." ) )
            {
                binPath = new BinPathGroup( this, configuration );
                bool success = configuration.DiscoverAssembliesFromPath
                                ? binPath.DiscoverFolder( monitor )
                                : configuration.Assemblies.Aggregate( true, ( success, b ) => success &= binPath.AddExplicit( monitor, b ) );
                Throw.DebugAssert( success == binPath.Success );
                _binPaths.Add( k, binPath );
                collector.Add( binPath );
            }
        }
        else
        {
            if( binPath.Success )
            {
                monitor.Info( $"BinPath configuration '{configuration.Name}' is shared with '{binPath.Configurations.First().Name}'." );
            }
            binPath.AddConfiguration( configuration );
        }
        return binPath;
    }

    CachedAssembly? Find( string name ) => _assemblies.GetValueOrDefault( name );

    CachedAssembly FindOrCreate( Assembly assembly, AssemblyName? knownName, DateTime? knownLastWriteTime, out AssemblyKind? initialKind )
    {
        if( !_assemblies.TryGetValue( assembly, out var cached ) )
        {
            GetAssemblyNames( knownName ?? assembly.GetName(), out string assemblyName, out string assemblyFullName );
            initialKind = AssemblyKind.None;
            if( CachedAssembly.IsSystemSkipped( assemblyName ) )
            {
                initialKind = AssemblyKind.SystemSkipped;
            }
            else if( _assemblyExcluder != null && _assemblyExcluder( assemblyName ) )
            {
                initialKind = AssemblyKind.Excluded;
            }
            cached = DoCreateAndCache( assembly, assemblyName, assemblyFullName, initialKind.Value, knownLastWriteTime );
            return cached;
        }
        initialKind = null;
        return cached;

        static void GetAssemblyNames( AssemblyName aName, out string assemblyName, out string assemblyFullName )
        {
            assemblyName = aName.Name!;
            assemblyFullName = aName.FullName;
            if( string.IsNullOrWhiteSpace( assemblyName ) || string.IsNullOrWhiteSpace( assemblyFullName ) )
            {
                Throw.ArgumentException( "Invalid assembly: the AssemmblyName.Name or assemblyName.FullName is null, empty or whitespace." );
            }
        }

    }

    CachedAssembly DoCreateAndCache( Assembly assembly,
                                     string assemblyName,
                                     string assemblyFullName,
                                     AssemblyKind initialKind,
                                     DateTime? knownLastWriteTime )
    {
        CachedAssembly? cached;
        if( !knownLastWriteTime.HasValue )
        {
            var tryPath = $"{_appContextPath}{assemblyName}.dll";
            knownLastWriteTime = File.Exists( tryPath )
                                    ? File.GetLastWriteTimeUtc( tryPath )
                                    : Util.UtcMinValue;
        }

        var lastWriteTime = knownLastWriteTime.Value;
        cached = new CachedAssembly( assembly, assemblyName, assemblyFullName, initialKind, lastWriteTime, isInitialAssembly: !_registrationClosed );
        _assemblies.Add( assembly, cached );
        if( _assemblies.ContainsKey( assemblyName ) )
        {
            Throw.CKException( $"""
                                    Duplicate assembly name '{assemblyName}': an assembly with this name has already been registered:
                                    - FullName: '{assemblyFullName}', LastWriteTime: {lastWriteTime}.
                                    - FullName: '{cached.FullName}', LastWriteTime: {cached.LastWriteTimeUtc}.
                                    """ );
        }
        _assemblies.Add( assemblyName, cached );
        return cached;
    }
}
