using CK.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CK.Setup;

/// <summary>
/// Defines the configuration of the CKEngine.
/// <para>
/// There is always at least one <see cref="BinPathConfiguration"/> in <see cref="BinPaths"/> (the <see cref="FirstBinPath"/>).
/// </para>
/// </summary>
public sealed partial class EngineConfiguration
{
    /// <summary>
    /// Default generated assembly name.
    /// <para>
    /// The <see cref="GeneratedAssemblyName"/> is either "CK.GeneratedAssembly" or starts
    /// with "CK.GeneratedAssembly.".
    /// </para>
    /// </summary>
    public const string GeneratedAssemblyNamePrefix = "CK.GeneratedAssembly";

    internal const string DefaultBinPathName = "AutoName";

    readonly List<BinPathConfiguration> _binPaths;
    readonly HashSet<Type> _globalExcludedTypes;
    readonly TypeConfigurationSet _globalTypes;
    readonly HashSet<string> _excludedAssemblies;
    Dictionary<string, EngineAspectConfiguration> _namedAspects;
    List<EngineAspectConfiguration> _aspects;
    string? _generatedAssemblyName;
    string? _informationalVersion;
    NormalizedPath _basePath;
    bool _revertOrderingNames;
    bool _traceDependencySorterInput;
    bool _traceDependencySorterOutput;

    /// <summary>
    /// Initializes a new empty configuration with a <see cref="FirstBinPath"/>.
    /// </summary>
    public EngineConfiguration()
    {
        _namedAspects = new Dictionary<string, EngineAspectConfiguration>();
        _aspects = new List<EngineAspectConfiguration>();
        _binPaths = new List<BinPathConfiguration>();
        _globalExcludedTypes = new HashSet<Type>();
        _globalTypes = new TypeConfigurationSet();
        _excludedAssemblies = new HashSet<string>();
        AddFirstBinPath();
    }

    void AddFirstBinPath()
    {
        var first = new BinPathConfiguration();
        first.Owner = this;
        _binPaths.Add( first );
        first.Name = "First";
    }

    /// <summary>
    /// Helper to load a configuration file.
    /// If no <see cref="EngineConfiguration.BasePath"/> exists in the file, the absolute <paramref name="path"/>'s directory is set
    /// as the base path.
    /// <para>
    /// The configuration must be valid, absolutely no exceptions are handled here, for instance all <c>&lt;Type&gt;Some.Type, From.Assembly&lt;/Type&gt;</c>
    /// must be resolved sucessfully.
    /// </para>
    /// </summary>
    /// <param name="path">The path of the xml file to load.</param>
    /// <returns>A configuration object.</returns>
    public static EngineConfiguration Load( string path )
    {
        path = System.IO.Path.GetFullPath( path );
        var c = new EngineConfiguration( XElement.Load( path ) );
        if( c.BasePath.IsEmptyPath ) c.BasePath = System.IO.Path.GetDirectoryName( path );
        return c;
    }

    /// <summary>
    /// Gets the list of all configuration aspects that must participate to setup.
    /// </summary>
    public IReadOnlyList<EngineAspectConfiguration> Aspects => _aspects;

    /// <summary>
    /// Finds an existing aspect or returns null.
    /// </summary>
    /// <param name="name">The aspect name.</param>
    /// <returns>The aspect or null.</returns>
    public EngineAspectConfiguration? FindAspect( string name ) => _namedAspects.GetValueOrDefault( name );

    /// <summary>
    /// Finds an existing aspect or returns null.
    /// </summary>
    /// <returns>The aspect or null.</returns>
    public T? FindAspect<T>() where T : EngineAspectConfiguration => _aspects.OfType<T>().SingleOrDefault();

    /// <summary>
    /// Ensures that an aspect is registered in <see cref="Aspects"/>.
    /// </summary>
    /// <typeparam name="T">The aspect type.</typeparam>
    /// <returns>The found or created aspect.</returns>
    public T EnsureAspect<T>() where T : EngineAspectConfiguration, new()
    {
        T? a = FindAspect<T>();
        if( a == null )
        {
            AddAspect( a = new T() );
        }
        return a;
    }

    /// <summary>
    /// Adds an aspect to these <see cref="Aspects"/>.
    /// No existing aspect with the same type must exist and the aspect must not belong to
    /// another configuration otherwise an <see cref="ArgumentException"/> is thrown.
    /// </summary>
    /// <param name="aspect">An aspect configuration to add.</param>
    public void AddAspect( EngineAspectConfiguration aspect )
    {
        Throw.CheckNotNullArgument( aspect );
        if( aspect.Owner == this ) return;

        Throw.CheckArgument( aspect.Owner == null );
        Throw.CheckArgument( "An aspect of the same type already exists.", !_namedAspects.ContainsKey( aspect.AspectName ) );
        aspect.Owner = this;
        _aspects.Add( aspect );
        _namedAspects.Add( aspect.AspectName, aspect );
    }

    /// <summary>
    /// Removes an aspect from <see cref="Aspects"/>. Does nothing if the <paramref name="aspect"/>
    /// does not belong to this configuration.
    /// </summary>
    /// <param name="aspect">An aspect configuration to remove.</param>
    public void RemoveAspect( EngineAspectConfiguration aspect )
    {
        Throw.CheckArgument( aspect != null );
        if( aspect.Owner == this )
        {
            _aspects.Remove( aspect );
            aspect.Owner = null;
            _namedAspects.Remove( aspect.AspectName );
            foreach( var b in _binPaths )
            {
                b.RemoveAspect( aspect.AspectName );
            }
        }
    }

    /// <summary>
    /// Gets or sets the final Assembly name.
    /// It must be <see cref="GeneratedAssemblyNamePrefix"/> (the default "CK.GeneratedAssembly") or
    /// start with "CK.GeneratedAssembly.".
    /// <para>
    /// This is a global configuration that applies to all the <see cref="BinPaths"/>.
    /// </para>
    /// </summary>
    [AllowNull]
    public string GeneratedAssemblyName
    {
        get => _generatedAssemblyName ?? GeneratedAssemblyNamePrefix;
        set
        {
            if( !String.IsNullOrWhiteSpace( value ) )
            {
                if( FileUtil.IndexOfInvalidFileNameChars( value ) >= 0 )
                {
                    Throw.ArgumentException( nameof( value ), $"Invalid file character in file name '{value}'." );
                }
                Throw.CheckArgument( value == GeneratedAssemblyNamePrefix
                                     || (value.Length > GeneratedAssemblyNamePrefix.Length + 2
                                         && value[GeneratedAssemblyNamePrefix.Length] == '.'
                                         && value.StartsWith( GeneratedAssemblyNamePrefix )) );
                _generatedAssemblyName = value;
            }
            else _generatedAssemblyName = null;
        }
    }

    /// <summary>
    /// Gets or sets the <see cref="System.Diagnostics.FileVersionInfo.ProductVersion"/> of
    /// the generated assembly.
    /// Defaults to null (no <see cref="System.Reflection.AssemblyInformationalVersionAttribute"/> should be generated).
    /// This is a global configuration that applies to all the <see cref="BinPaths"/>.
    /// </summary>
    public string? InformationalVersion { get => _informationalVersion; set => _informationalVersion = value; }

    /// <summary>
    /// Gets or sets whether the ordering of StObj that share the same rank in the dependency graph must be inverted.
    /// Defaults to false.
    /// This is a global configuration that applies to all the <see cref="BinPaths"/>.
    /// </summary>
    public bool RevertOrderingNames { get => _revertOrderingNames; set => _revertOrderingNames = value; }

    /// <summary>
    /// Gets or sets whether the dependency graph (the set of IDependentItem) associated
    /// to the StObj objects must be send to the monitor before sorting.
    /// Defaults to false.
    /// This is a global configuration that applies to all the <see cref="BinPaths"/>.
    /// </summary>
    public bool TraceDependencySorterInput { get => _traceDependencySorterInput; set => _traceDependencySorterInput = value; }

    /// <summary>
    /// Gets or sets whether the dependency graph (the set of ISortedItem) associated
    /// to the StObj objects must be send to the monitor once the graph is sorted.
    /// Defaults to false.
    /// This is a global configuration that applies to all the <see cref="BinPaths"/>.
    /// </summary>
    public bool TraceDependencySorterOutput { get => _traceDependencySorterOutput; set => _traceDependencySorterOutput = value; }

    /// <summary>
    /// Gets or sets an optional base path that applies to relative <see cref="BinPaths"/>.
    /// When empty, the current directory is used. When this configuration is the result of a <see cref="Load(string)"/>,
    /// the file's directory is the initial base path.
    /// </summary>
    public NormalizedPath BasePath { get => _basePath; set => _basePath = value; }

    #region BinPaths

    /// <summary>
    /// Gets the first <see cref="BinPathConfiguration"/> from the <see cref="BinPaths"/>
    /// that is guaranteed to exist.
    /// </summary>
    public BinPathConfiguration FirstBinPath => _binPaths[0];

    /// <summary>
    /// Gets the binary paths to setup.
    /// <para>
    /// There is always at least one <see cref="BinPathConfiguration"/>
    /// in this list (<see cref="RemoveBinPath(BinPathConfiguration)"/> never removes the single one).
    /// </para>
    /// </summary>
    public IReadOnlyList<BinPathConfiguration> BinPaths => _binPaths;

    /// <summary>
    /// Finds the <see cref="BinPathConfiguration"/> or throws a <see cref="ArgumentException"/> if not found.
    /// </summary>
    /// <param name="binPathName">The bin path name. Must be an existing BinPath or a <see cref="ArgumentException"/> is thrown.</param>
    /// <returns>The BinPath.</returns>
    public BinPathConfiguration FindRequiredBinPath( string binPathName )
    {
        var b = FindBinPath( binPathName );
        if( b == null )
        {
            Throw.ArgumentException( nameof( binPathName ),
                                     $"""
                                      Unable to find BinPath named '{binPathName}'. Existing BinPaths are:
                                      '{_binPaths.Select( b => b.Name ).Concatenate( "', '" )}'.
                                      """ );
        }
        return b;
    }

    /// <summary>
    /// Tries to find the <see cref="BinPathConfiguration"/> or returns null.
    /// </summary>
    /// <param name="binPathName">The bin path name.</param>
    /// <returns>The BinPath or null.</returns>
    public BinPathConfiguration? FindBinPath( string binPathName ) => _binPaths.FirstOrDefault( b => b.Name == binPathName );

    /// <summary>
    /// Adds a BinPathConfiguration to these <see cref="BinPaths"/>.
    /// The <see cref="BinPathConfiguration.Name"/> is automatically adusted if the name already exists.
    /// <para>
    /// The <paramref name="binPath"/> must not belong to another Engine configuration otherwise
    /// a <see cref="ArgumentException"/> is thrown.
    /// </para>
    /// <para>
    /// <see cref="BinPathAspectConfiguration"/> that cannot be bound to an existing <see cref="Aspects"/>
    /// are removed from the <see cref="BinPathConfiguration.Aspects"/>.
    /// </para>
    /// </summary>
    /// <param name="binPath">A BinPath configuration to add.</param>
    public void AddBinPath( BinPathConfiguration binPath )
    {
        Throw.CheckNotNullArgument( binPath );
        if( binPath.Owner == this ) return;

        Throw.CheckArgument( binPath.Owner == null );
        binPath.Owner = this;
        _binPaths.Add( binPath );
        EnsureUniqueName( binPath );
        // Remove orphans BinPath aspect configurations or bind them.
        List<BinPathAspectConfiguration>? toRemove = null;
        foreach( var aspect in binPath.Aspects )
        {
            var a = FindAspect( aspect.AspectName );
            if( a != null )
            {
                Throw.DebugAssert( aspect.Owner == binPath );
                aspect.Bind( binPath, a );
            }
            else
            {
                toRemove ??= new List<BinPathAspectConfiguration>();
                toRemove.Add( aspect );
            }
        }
        if( toRemove != null )
        {
            foreach( var aspect in toRemove )
            {
                binPath.RemoveAspect( aspect );
            }
        }
    }

    internal static string CheckBinPathName( string? name )
    {
        if( name == null ) return DefaultBinPathName;
        if( !Regex.IsMatch( name, @"^[a-zA-Z_0-9]+$", RegexOptions.CultureInvariant ) )
        {
            Throw.ArgumentException( nameof( name ), $"BinPath name '{name}' must not be empty only contain A-Z, a-z, _ and 0-9 characters." );
        }
        return name;
    }

    internal void EnsureUniqueName( BinPathConfiguration binPath )
    {
        var n = binPath.Name;
        if( !_binPaths.Any( b => b != binPath && n == b.Name ) )
        {
            return;
        }
        n = Regex.Replace( n, @"\d+$", String.Empty, RegexOptions.CultureInvariant );
        int i = 0;
        var nNum = n;
        while( _binPaths.Any( b => b != binPath && nNum == b.Name ) )
        {
            ++i;
            nNum = n + i.ToString();
        }
        binPath.Name = nNum;
    }

    /// <summary>
    /// Removes a BinPathConfiguration from <see cref="BinPaths"/>. Does nothing if the <paramref name="binPath"/>
    /// does not belong to this configuration or if there is only a single BinPath.
    /// </summary>
    /// <param name="binPath">A BinPath configuration to remove.</param>
    public void RemoveBinPath( BinPathConfiguration binPath )
    {
        Throw.CheckArgument( binPath != null );
        if( binPath.Owner == this && _binPaths.Count > 1 )
        {
            _binPaths.Remove( binPath );
            binPath.Owner = null;
            foreach( var aspect in binPath.Aspects )
            {
                aspect.Bind( binPath, null );
            }
        }
    }

    #endregion // BinPaths

    /// <summary>
    /// Gets a mutable set of of simple assembly names that must be excluded from registration.
    /// This applies to all <see cref="BinPaths"/> and there is no exclusion at <see cref="BinPathConfiguration"/> level.
    /// </summary>
    public HashSet<string> ExcludedAssemblies => _excludedAssemblies;

    /// <summary>
    /// Gets a mutable set of type that must be excluded from registration.
    /// This applies to all <see cref="BinPaths"/>: excluding a type here guaranties its exclusion
    /// from any BinPath.
    /// </summary>
    public HashSet<Type> GlobalExcludedTypes => _globalExcludedTypes;

    /// <summary>
    /// Gets the set of types that must be added to all <see cref="BinPaths"/> in their <see cref="BinPathConfiguration.Types"/>.
    /// <para>
    /// The <see cref="GlobalExcludedTypes"/> has the priority over this set: an excluded type will be removed from this one.
    /// </para>
    /// </summary>
    public TypeConfigurationSet GlobalTypes => _globalTypes;

    /// <summary>
    /// Gets or sets a base SHA1 for the StObjMaps (CKSetup sets this to the files signature).
    /// <para>
    /// By defaults, when <see cref="SHA1Value.Zero"/> or <see cref="SHA1Value.Empty"/>, the final signatures
    /// of each BinPath are computed from the generated source code.
    /// </para>
    /// <para>
    /// When set to non zero, the BinPaths' signature are the hashes of this property and the normalized <see cref="BinPathConfiguration.Path"/>.
    /// </para>
    /// </summary>
    public SHA1Value BaseSHA1 { get; set; }

    /// <summary>
    /// Gets whether caching may occur at any level (based on <see cref="BaseSHA1"/>) or if
    /// a run must be done regardless of any previous states.
    /// Defaults to false.
    /// </summary>
    public bool ForceRun { get; set; }


    /// <summary>
    /// Clones this configuration (via xml).
    /// </summary>
    /// <returns>A clone of this configuration.</returns>
    public EngineConfiguration Clone() => new EngineConfiguration( ToXml() );
}
