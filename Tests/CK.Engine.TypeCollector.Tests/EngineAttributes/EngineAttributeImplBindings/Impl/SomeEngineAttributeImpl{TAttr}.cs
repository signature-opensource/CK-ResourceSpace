using System.Runtime.CompilerServices;

namespace CK.Engine.TypeCollector.Tests;

/// <summary>
/// Strongly typed Attribute adapter.
/// </summary>
/// <typeparam name="TAttr">The attribute type that specializes SomeEngineAttribute.</typeparam>
public class SomeEngineAttributeImpl<TAttr> : SomeEngineAttributeImpl
    where TAttr : SomeEngineAttribute
{
    /// <summary>
    /// Gets the strongly typed attribute.
    /// </summary>
    public new TAttr Attribute => Unsafe.As<TAttr>( base.Attribute );
}
