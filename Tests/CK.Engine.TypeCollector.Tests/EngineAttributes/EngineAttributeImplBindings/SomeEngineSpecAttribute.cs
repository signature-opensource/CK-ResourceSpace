using System;

namespace CK.Engine.TypeCollector.Tests;

/// <summary>
/// Specialized attribute that can be bound to:
/// <list type="bullet">
///     <item>
///     The same implementation as its base (SomePrimaryMemberAttributeImpl).
///     It is up to this implementation to handle this specialized attribute (this can
///     be useful is some -simple- scenario).
///     </item>
///     <item>
///     A specialized implementation of the base SomePrimaryMemberAttributeImpl.
///     The <see cref="SomeEngineSpecAttributeImpl"/> inherits SomePrimaryMemberAttributeImpl.
///     This is useful to exend the behavior of a Attribute/Impl in other packages
///     (providing that the attribute is not sealed and the base Impl is also not sealed
///     and has some useful extension points).
///     </item>
///     <item>
///     A completely different implementation (here <see cref="SomePrimaryMemberAttributeSpecDifferentImpl"/>).
///     </item>
/// </list>
/// </summary>
[AttributeUsage( Targets, AllowMultiple = true, Inherited = false )]
public sealed class SomeEngineSpecAttribute : SomeEngineAttribute
{
    /// <summary>
    /// Option to the base "Spec" attribute.
    /// </summary>
    public int SomethingMore { get; }

    /// <summary>
    /// Demo of different possible bindings.
    /// This obviously is a demo (and a test), in real life this should hardly appear!
    /// </summary>
    public enum ImplBinding
    {
        SameAsBase,
        SpecializedImpl,
        DifferentImpl
    }

    public SomeEngineSpecAttribute( string name, ImplBinding binding, int somethingMore )
        : base( binding switch
                {
                    ImplBinding.SameAsBase => "CK.Engine.TypeCollector.Tests.SomeEngineAttributeImpl, CK.Engine.TypeCollector.Tests",
                    ImplBinding.SpecializedImpl => "CK.Engine.TypeCollector.Tests.SomeEngineSpecAttributeImpl, CK.Engine.TypeCollector.Tests",
                    _ => "CK.Engine.TypeCollector.Tests.SomeEngineSpecAttributeDifferentImpl, CK.Engine.TypeCollector.Tests"
                },
                name + "[Spec]" )
    {
        SomethingMore = somethingMore;
    }
}
