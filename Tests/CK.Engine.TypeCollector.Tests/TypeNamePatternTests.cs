using CK.Core;
using CK.Setup;
using NUnit.Framework;
using Shouldly;
using System;

namespace CK.Engine.TypeCollector.Tests;

[TestFixture]
public class TypeNamePatternTests
{
    public sealed class Nested { }

    [TestCase( "CK.Engine.TypeCollector.Tests.*, CK.Engine.TypeCollector.Tests", true )]
    [TestCase( "CK.Engine.TypeCollector.*, CK.Engine.TypeCollector.Tests", true )]
    [TestCase( "CK.Engine.TypeCollector.Tests.TypeNamePatternTests*, CK.Engine.TypeCollector.Tests", true )]
    [TestCase( "*, CK.Engine.TypeCollector.Tests", true )]
    [TestCase( "CK.Engine.*, CK.Engine.*", true )]
    [TestCase( "CK.Engine.TypeCollector.Tests.TypeNamePatternTests+Nested, CK.Engine.*", true )]
    [TestCase( "CK.Other.*, CK.Engine.TypeCollector.Tests", false )]
    [TestCase( "*, CK.Engine.TypeCollector", false )]
    [TestCase( "CK.Engine.TypeCollector.Tests.*, Other.*", false )]
    public void matches( string pattern, bool expected )
    {
        TypeNamePattern.Parse( pattern ).Matches( typeof( Nested ) ).ShouldBe( expected );
    }

    [TestCase( "CK.*", "a single comma" )]
    [TestCase( "CK.*, A, B", "a single comma" )]
    [TestCase( "*CK, A", "a '*' can only end the type" )]
    [TestCase( "CK.*.X, A", "a '*' can only end the type" )]
    [TestCase( "CK.X, *A", "a '*' can only end the assembly" )]
    [TestCase( "CK.X, A", "it contains no '*'" )]
    [TestCase( "CK.*, *", "the assembly cannot be '*' alone" )]
    [TestCase( " , A*", "the type is missing" )]
    public void invalid_patterns( string pattern, string error )
    {
        TypeNamePattern.TryParse( pattern, out _, out var message ).ShouldBeFalse();
        message.ShouldContain( error );
        Util.Invokable( () => TypeNamePattern.Parse( pattern ) ).ShouldThrow<FormatException>();
    }

    [Test]
    public void the_pattern_is_normalized()
    {
        TypeNamePattern.Parse( "  CK.X.* ,CK.A " ).ToString().ShouldBe( "CK.X.*, CK.A" );
        TypeNamePattern.IsPattern( "CK.X.*, A" ).ShouldBeTrue();
        TypeNamePattern.IsPattern( "CK.X, A" ).ShouldBeFalse();
    }
}
