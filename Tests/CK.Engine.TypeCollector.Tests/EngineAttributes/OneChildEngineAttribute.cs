using CK.Core;
using System;

namespace CK.Engine.TypeCollector.Tests;

[AttributeUsage( AttributeTargets.Interface, AllowMultiple = true, Inherited = false )]
public class OneChildEngineAttribute : ChildEngineAttribute<OneEngineAttribute>
{
    public OneChildEngineAttribute( string name )
        : base( "CK.Engine.TypeCollector.Tests.OneChildEngineAttributeImpl, CK.Engine.TypeCollector.Tests" )
    {
        Name = name;
    }

    public string Name { get; }
}
