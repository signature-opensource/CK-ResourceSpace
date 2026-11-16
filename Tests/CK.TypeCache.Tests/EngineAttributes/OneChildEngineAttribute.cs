using CK.Core;
using System;

namespace CK.TypeCache.Tests;

[AttributeUsage( AttributeTargets.Interface, AllowMultiple = true, Inherited = false )]
public class OneChildEngineAttribute : ChildEngineAttribute<OneEngineAttribute>
{
    public OneChildEngineAttribute( string name )
        : base( "CK.TypeCache.Tests.OneChildEngineAttributeImpl, CK.TypeCache.Tests" )
    {
        Name = name;
    }

    public string Name { get; }
}
