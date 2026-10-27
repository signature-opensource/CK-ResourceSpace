using CK.Core;
using NUnit.Framework;
using Shouldly;
using static CK.Testing.MonitorTestHelper;

namespace CK.ResourceSpace.Tests;

/// <summary>
/// A failed RegisterPackage (the error is logged and null is returned) is remembered: the
/// registrations are incomplete, so the build fails even if the caller ignored the null.
/// </summary>
[TestFixture]
public class RegistrationErrorTests
{
    [Test]
    public void a_failed_registration_on_the_configuration_fails_its_Build()
    {
        var config = new ResSpaceConfiguration();
        config.RegisterPackage( TestHelper.Monitor, typeof( P1.Package ) ).ShouldNotBeNull();
        // Duplicate registration.
        config.RegisterPackage( TestHelper.Monitor, typeof( P1.Package ) ).ShouldBeNull();

        config.Build( TestHelper.Monitor ).ShouldBeNull();
    }

    [Test]
    public void a_failed_registration_on_the_collector_fails_the_data_build()
    {
        var config = new ResSpaceConfiguration();
        config.RegisterPackage( TestHelper.Monitor, typeof( P1.Package ) ).ShouldNotBeNull();
        var collector = config.Build( TestHelper.Monitor ).ShouldNotBeNull();
        // Duplicate registration.
        collector.RegisterPackage( TestHelper.Monitor, typeof( P1.Package ) ).ShouldBeNull();

        new ResSpaceDataBuilder( collector ).Build( TestHelper.Monitor ).ShouldBeNull();
    }
}
