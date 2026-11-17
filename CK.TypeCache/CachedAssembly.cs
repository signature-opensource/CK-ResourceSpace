using CK.Core;
using CK.Setup;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;

namespace CK.TypeCache;

/// <summary>
/// Cached assembly. Can be an initially discovered one or a cached instance obtained later.
/// </summary>
public sealed class CachedAssembly : IComparable<CachedAssembly>
{
    readonly Assembly _assembly;
    readonly string _assemblyName;
    readonly string _assemblyFullName;
    readonly DateTime _lastWriteTime;
    readonly bool _isInitialAssembly;

    // Initialized on demand.
    ImmutableArray<CustomAttributeData> _customAttributes;
    // Initialized by AssemblyCache.DoAdd.
    internal AssemblyKind _kind;
    internal ImmutableArray<CachedAssembly> _rawReferencedAssembly;
    internal IReadOnlySet<CachedAssembly> _models;
    internal IReadOnlySet<CachedAssembly> _allModels;
    // Initialized by CollectTypes.
    internal ConfiguredTypeSet? _types;

    internal CachedAssembly( Assembly assembly,
                             string name,
                             string fullName,
                             AssemblyKind initialKind,
                             DateTime lastWriteTime,
                             bool isInitialAssembly )
    {
        _assembly = assembly;
        _assemblyName = name;
        _assemblyFullName = fullName;
        _kind = initialKind;
        _lastWriteTime = lastWriteTime;
        _isInitialAssembly = isInitialAssembly;
        _models = ImmutableHashSet<CachedAssembly>.Empty;
        _allModels = ImmutableHashSet<CachedAssembly>.Empty;
        Throw.DebugAssert( "These comes from the configuration.", _kind is AssemblyKind.None or AssemblyKind.SystemSkipped or AssemblyKind.Excluded );
        if( initialKind is not AssemblyKind.SystemSkipped )
        {
            _kind = GetInitialAssemblyType( initialKind is AssemblyKind.Excluded );
            // Skipped or post registration assembly: we can settle the types and references.
            if( !isInitialAssembly || _kind is AssemblyKind.AutoSkipped )
            {
                _rawReferencedAssembly = ImmutableArray<CachedAssembly>.Empty;
            }
            // An Excluded Engine is an error: this will be handled right after.
        }
        else
        {
            _customAttributes = ImmutableArray<CustomAttributeData>.Empty;
            _rawReferencedAssembly = ImmutableArray<CachedAssembly>.Empty;
        }
    }

    AssemblyKind GetInitialAssemblyType( bool excluded )
    {
        // Initial type detection. Allocates the CustomAttributes.
        uint found = 0;
        foreach( var d in CustomAttributes )
        {
            // Skipped: nothing more to do.
            // This hides the "multiple error" below but we don't care.
            if( d.AttributeType == typeof( SkippedAssemblyAttribute ) )
            {
                return AssemblyKind.AutoSkipped;
            }
            else if( d.AttributeType.Name == nameof( IsModelAttribute ) ) found |= 1;
            else if( d.AttributeType.Name == nameof( IsEngineAttribute ) ) found |= 2;
            else if( d.AttributeType.Name == nameof( IsModelDefinerAttribute ) ) found |= 4;
        }
        if( BitOperations.PopCount( found ) > 1 )
        {
            // We cannot reaaly do anything else here... We cannot exclude the assembly, nor choose one at random because there is no sensible choice.
            // This is a fucked up assembly: the developper must fix this. 
            Throw.CKException( $"""
                                Invalid assembly '{_assemblyName}': it contains more than one [IsModel], [IsEngine] or [IsModelDefiner].
                                These attributes are mutually exclusive.
                                """ );
        }
        var k = found switch { 1 => AssemblyKind.Model, 2 => AssemblyKind.Engine, 4 => AssemblyKind.ModelDefiner, _ => AssemblyKind.None };
        if( excluded ) k |= AssemblyKind.Excluded;
        return k;
    }

    /// <summary>
    /// Comparing two assemblies is based on the <see cref="Name"/>. This may seem surprising but this is
    /// enough to order the <see cref="Models"/> that makes type sets composition deterministic AND we are working
    /// with unique assembly names.
    /// </summary>
    /// <param name="other">The other cached assembly.</param>
    /// <returns>See <see cref="string.CompareTo(string?)"/>.</returns>
    public int CompareTo( CachedAssembly? other ) => _assemblyName.CompareTo( other?._assemblyName );

    /// <summary>
    /// Gets this assembly's simple name.
    /// </summary>
    public string Name => _assemblyName;

    /// <summary>
    /// Gets the engines that this assembly requires: its <see cref="RequiredEngineAttribute"/> (the name of the engine
    /// assembly and its optional NuGet version range).
    /// </summary>
    public IEnumerable<(string Name, string? VersionRange)> RequiredEngines
    {
        get
        {
            foreach( var a in CustomAttributes )
            {
                if( a.AttributeType.Name == nameof( RequiredEngineAttribute )
                    && a.ConstructorArguments.Count >= 1
                    && a.ConstructorArguments[0].Value is string name )
                {
                    yield return (name, a.ConstructorArguments.Count > 1 ? a.ConstructorArguments[1].Value as string : null);
                }
            }
        }
    }

    /// <summary>
    /// Gets this assembly's full name.
    /// </summary>
    public string FullName => _assemblyFullName;

    /// <summary>
    /// Gets the assembly. There should be few reasons to need it.
    /// </summary>
    public Assembly Assembly => _assembly;

    /// <summary>
    /// Gets the last write time of the file if the assembly file exists in <see cref="AppContext.BaseDirectory"/>,
    /// otherwise <see cref="Util.UtcMinValue"/>.
    /// </summary>
    public DateTime LastWriteTimeUtc => _lastWriteTime;

    /// <summary>
    /// Gets this kind of assembly.
    /// </summary>
    public AssemblyKind Kind => _kind;

    /// <summary>
    /// Gets the custom attributes data. Assembly attributes are not instantiated,
    /// we exploit only the more efficient CustomAttributeData.
    /// </summary>
    public ImmutableArray<CustomAttributeData> CustomAttributes
    {
        get
        {
            if( _customAttributes.IsDefault )
            {
                _customAttributes = _assembly.CustomAttributes.ToImmutableArray();
            }
            return _customAttributes;
        }
    }

    /// <summary>
    /// Gets all the directly referenced assemblies, regardless of their <see cref="Kind"/> and
    /// any <see cref="RemoveModelAttribute"/> that this assembly can define.
    /// <para>
    /// This is always empty when <see cref="AssemblyKind.Excluded"/>, <see cref="AssemblyKind.AutoSkipped"/> or <see cref="AssemblyKind.SystemSkipped"/>
    /// or when <see cref="IsInitialAssembly"/> is false.
    /// </para>
    /// </summary>
    public ImmutableArray<CachedAssembly> RawReferencedAssemblies => _rawReferencedAssembly;

    /// <summary>
    /// Gets all the <see cref="AssemblyKind.Model"/> that this Model references. 
    /// <para>
    /// This is always empty when <see cref="AssemblyKind.Excluded"/>, <see cref="AssemblyKind.AutoSkipped"/> or <see cref="AssemblyKind.SystemSkipped"/>
    /// or when <see cref="IsInitialAssembly"/> is false.
    /// </para>
    /// </summary>
    public IReadOnlySet<CachedAssembly> AllModels => _allModels;

    /// <summary>
    /// Gets the curated closure of <see cref="AssemblyKind.Model"/>, considering any <see cref="RemoveModelAttribute"/>
    /// defined by this assembly and by referenced ones: a referenced assembly doesn't appear here if it has been excluded by
    /// this assembly or if it is an indirect reference that has been excluded by all the intermediate referencers.
    /// <para>
    /// Stated differently:
    /// <list type="bullet">
    ///     <item>An assembly is excluded if everybody agrees to exclude it.</item>
    ///     <item>A parent assembly can always exclude any of its referenced assemblies, even assemblies that it doesn't reference directly.</item>
    /// </list>
    /// </para>
    /// This is always empty when <see cref="AssemblyKind.Excluded"/>, <see cref="AssemblyKind.AutoSkipped"/> or <see cref="AssemblyKind.SystemSkipped"/>
    /// or when <see cref="IsInitialAssembly"/> is false.
    /// </summary>
    public IReadOnlySet<CachedAssembly> Models => _models;

    /// <summary>
    /// Gets whether this assembly has been been discovered by the initial registration or is
    /// an assembly that comes from a later type registration.
    /// </summary>
    public bool IsInitialAssembly => _isInitialAssembly;

    /// <summary>
    /// Gets the types that this Model brings to the system.
    /// <para>
    /// This is null if this assembly is not a Model or if it is not used.
    /// </para>
    /// </summary>
    public IConfiguredTypeSet? Types => _types;

    internal void AddHash( IncrementalHash hasher )
    {
        hasher.Append( _assemblyName );
        hasher.Append( _lastWriteTime );
    }

    /// <summary>
    /// Filters out "Microsoft.*", "System.*", "Azure.*" and "DotNet.*".  
    /// </summary>
    /// <param name="assemblyName">The simple assembly name.</param>
    /// <returns>True if the assembly name should be <see cref="AssemblyKind.SystemSkipped"/>.</returns>
    public static bool IsSystemSkipped( string assemblyName )
    {
        return assemblyName.StartsWith( "Microsoft." )
               || assemblyName.StartsWith( "System." )
               || assemblyName.StartsWith( "Azure." )
               || assemblyName.StartsWith( "DotNet." );
    }

    /// <summary>
    /// Gets "Assembly '<see cref="Name"/>".
    /// </summary>
    /// <returns>"Assembly 'Name'"</returns>
    public override string ToString() => $"Assembly '{Name}'";
}
