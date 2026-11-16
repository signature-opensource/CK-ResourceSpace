using CK.Core;
using System;

namespace CK.TypeCache.Tests;

[AttributeUsage( SomeEngineAttribute.Targets, AllowMultiple = true, Inherited = false )]
public class SomeChildEngineAttribute : ChildEngineAttribute<SomeEngineAttribute>
{
    public SomeChildEngineAttribute( string name )
        : base( "CK.TypeCache.Tests.SomeChildEngineAttributeImpl, CK.TypeCache.Tests" )
    {
        Name = name;
    }

    public string Name { get; }
}
