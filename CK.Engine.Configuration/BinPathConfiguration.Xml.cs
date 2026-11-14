using CK.Core;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Xml;
using System;

namespace CK.Setup;

public sealed partial class BinPathConfiguration
{
    internal BinPathConfiguration( EngineConfiguration configuration, XElement e, Dictionary<string, EngineAspectConfiguration> namedAspects, int idx )
    {
        _owner = configuration;
        _name = (string?)e.Attribute( EngineConfiguration.xName ) ?? (idx == 0 ? "First" : EngineConfiguration.DefaultBinPathName);
        _path = (string?)e.Attribute( EngineConfiguration.xPath );
        _outputPath = (string?)e.Element( EngineConfiguration.xOutputPath );
        _projectPath = (string?)e.Element( EngineConfiguration.xProjectPath );

        if( e.Element( "SkipCompilation" ) != null )
        {
            throw new XmlException( @"Element SkipCompilation must be replaced with CompileOption that can be be ""None"", ""Parse"" or ""Compile"". It defaults to ""None""." );
        }
        CompileOption = e.Element( EngineConfiguration.xCompileOption )?.Value.ToUpperInvariant() switch
        {
            null => CompileOption.None,
            "NONE" => CompileOption.None,
            "PARSE" => CompileOption.Parse,
            "COMPILE" => CompileOption.Compile,
            _ => throw new XmlException( @"Expected CompileOption to be ""None"", ""Parse"" or ""Compile""." )
        };

        GenerateSourceFiles = (bool?)e.Element( EngineConfiguration.xGenerateSourceFiles ) ?? true;

        _assemblies = new HashSet<string>( EngineConfiguration.StringsFromXml( e, EngineConfiguration.xAssemblies, EngineConfiguration.xAssembly ) );
        _discoverAssembliesFromPath = (bool?)e.Attribute( EngineConfiguration.xDiscoverAssembliesFromPath ) ?? false;
        _excludedTypes = new HashSet<Type>( EngineConfiguration.TypesFromXml( e, EngineConfiguration.xExcludedTypes, EngineConfiguration.xType ) );

        _types = new TypeConfigurationSet( e.Elements( EngineConfiguration.xTypes ) );
        _typeSets = new Dictionary<string, TypeSetConfiguration>( StringComparer.OrdinalIgnoreCase );
        foreach( var s in e.Elements( EngineConfiguration.xTypeSets ).Elements( EngineConfiguration.xTypeSet ) )
        {
            var set = new TypeSetConfiguration( s );
            if( !_typeSets.TryAdd( set.Name, set ) ) Throw.InvalidDataException( $"Duplicated TypeSet '{set.Name}' in BinPath '{_name}'." );
        }

        var allowedNames = new List<string>()
        {
            EngineConfiguration.xTypes.ToString(),
            EngineConfiguration.xExcludedTypes.ToString(),
            EngineConfiguration.xTypeSets.ToString(),
            EngineConfiguration.xAssemblies.ToString(),
            EngineConfiguration.xGenerateSourceFiles.ToString(),
            EngineConfiguration.xCompileOption.ToString(),
            EngineConfiguration.xProjectPath.ToString(),
            EngineConfiguration.xOutputPath.ToString(),
            EngineConfiguration.xPath.ToString(),
            EngineConfiguration.xName.ToString()
        };
        _aspects = new Dictionary<string, BinPathAspectConfiguration>();
        var aspectConfigurations = e.Elements().Where( e => !allowedNames.Contains( e.Name.LocalName ) ).ToList();
        foreach( var c in aspectConfigurations )
        {
            if( !namedAspects.TryGetValue( c.Name.LocalName, out var aspectType ) )
            {
                var expectedAspects = "";
                if( namedAspects.Count > 0 )
                {
                    expectedAspects = $" or aspect's BinPath configurations '{namedAspects.Keys.Concatenate( "', '" )}'";
                }
                Throw.InvalidDataException( $"Unexpected element name '{c.Name.LocalName}'. Expected: '{allowedNames.Concatenate( "', '" )}'{expectedAspects}." );
            }
            if( _aspects.ContainsKey( aspectType.AspectName ) )
            {
                Throw.InvalidDataException( $"Duplicated element name '{c.Name.LocalName}'. At most one BinPath aspect configuration of a given type can exist." );
            }
            var a = aspectType.CreateBinPathConfiguration();
            if( a.AspectName != aspectType.AspectName )
            {
                Throw.CKException( $"Aspect '{aspectType.AspectName}' created an aspect of type '{a.AspectName}BinPathAspectConfiguration'. The type name should be ''{aspectType.AspectName}BinPathAspectConfiguration''." );
            }
            a.InitializeFrom( c );
            a.Bind( this, aspectType );
            _aspects.Add( a.AspectName, a );
        }
    }

    /// <summary>
    /// Creates a xml element from this <see cref="BinPathConfiguration"/>.
    /// </summary>
    /// <returns>A new element.</returns>
    public XElement ToXml()
    {
        return new XElement( EngineConfiguration.xBinPath,
                                new XAttribute( EngineConfiguration.xName, Name ),
                                new XAttribute( EngineConfiguration.xPath, Path ),
                                new XAttribute( EngineConfiguration.xDiscoverAssembliesFromPath, DiscoverAssembliesFromPath ),
                                !OutputPath.IsEmptyPath
                                    ? new XElement( EngineConfiguration.xOutputPath, OutputPath )
                                    : null,
                                !ProjectPath.IsEmptyPath
                                    ? new XElement( EngineConfiguration.xProjectPath, ProjectPath )
                                    : null,
                                new XElement( EngineConfiguration.xCompileOption, CompileOption.ToString() ),
                                GenerateSourceFiles
                                    ? null
                                    : new XElement( EngineConfiguration.xGenerateSourceFiles, false ),
                                EngineConfiguration.ToXml( EngineConfiguration.xAssemblies, EngineConfiguration.xAssembly, Assemblies ),
                                EngineConfiguration.ToXml( EngineConfiguration.xExcludedTypes, EngineConfiguration.xType, ExcludedTypes ),
                                _types.ToXml( EngineConfiguration.xTypes ),
                                _typeSets.Count > 0
                                    ? new XElement( EngineConfiguration.xTypeSets, _typeSets.Values.Select( s => s.ToXml() ) )
                                    : null,
                                _aspects.Values.Select( a => a.ToXml() ) );
    }
}
