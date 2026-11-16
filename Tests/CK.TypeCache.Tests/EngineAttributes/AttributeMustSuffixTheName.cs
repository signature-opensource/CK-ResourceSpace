using CK.Core;

namespace CK.TypeCache.Tests;

public class AttributeMustSuffixTheName : EngineAttribute
{
    public AttributeMustSuffixTheName()
        : base( "Unused (name check fails)." )
    {
    }
}

public class AttributeMustSuffixTheNameT : ChildEngineAttribute<AttributeMustSuffixTheName>
{
    public AttributeMustSuffixTheNameT()
        : base( "Unused (name check fails)." )
    {
    }
}

// Correct secondary name that targets an invalid named primary attribute.
// The implementation type is instantiated.
public class UselessImpl : EngineAttributeImpl { }

public class CorrectlyNamedButBadParentAttribute : ChildEngineAttribute<AttributeMustSuffixTheName>
{
    public CorrectlyNamedButBadParentAttribute()
        : base( "CK.TypeCache.Tests.UselessImpl, CK.TypeCache.Tests" )
    {
    }
}

