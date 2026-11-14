using CK.Core;

namespace CK.Engine.TypeCollector.Tests;

public sealed class CanBeBuggyAttribute : EngineAttribute
{
    public static bool ImplOnInitializedThrow;
    public static bool ImplInitializeFalse;
    public static bool ImplOnInitializedFalse;

    public static void Reset()
    {
        ImplOnInitializedThrow = false;
        ImplInitializeFalse = false;
        ImplOnInitializedFalse = false;
    }

    public CanBeBuggyAttribute( string name )
        : base( "CK.Engine.TypeCollector.Tests.CanBeBuggyAttributeImpl, CK.Engine.TypeCollector.Tests" )
    {
        Name = name;
    }

    public string Name { get; }
}
