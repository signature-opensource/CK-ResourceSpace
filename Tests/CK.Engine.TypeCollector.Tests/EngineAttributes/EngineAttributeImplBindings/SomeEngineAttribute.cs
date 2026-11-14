using CK.Core;
using System;

namespace CK.Engine.TypeCollector.Tests;

/// <summary>
/// This attribute can be specialized. Specializations can be bound to a different implementation
/// than the default one thanks to the protected constructor.
/// </summary>
[AttributeUsage( Targets, AllowMultiple = false, Inherited = false )]
public class SomeEngineAttribute : EngineAttribute
{
    public const AttributeTargets Targets = AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Method;

    public SomeEngineAttribute( string name )
        : base( "CK.Engine.TypeCollector.Tests.SomeEngineAttributeImpl, CK.Engine.TypeCollector.Tests" )
    {
        Name = name;
    }

    protected SomeEngineAttribute( string actualAttributeTypeAssemblyQualifiedName, string name )
        : base( actualAttributeTypeAssemblyQualifiedName )
    {
        Name = name;
    }

    public string Name { get; init; }
}
