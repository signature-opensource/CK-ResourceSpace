using CK.Core;
using System;

namespace CK.TypeCache.Tests;

/// <summary>
/// This attributes can only decorate interfaces.
/// <para>
/// It specifies <c>AllowMultiple = true</c> that is weird but possible.
/// Usually root attributes are NOT multiple and <see cref="EngineAttribute{T}"/> are multiple.
/// </para>
/// </summary>
[AttributeUsage( AttributeTargets.Interface, AllowMultiple = true, Inherited = false )]
public class OneEngineAttribute : EngineAttribute
{
    public OneEngineAttribute( string name )
        : base( "CK.TypeCache.Tests.OneEngineAttributeImpl, CK.TypeCache.Tests" )
    {
        Name = name;
    }

    public string Name { get; }
}

