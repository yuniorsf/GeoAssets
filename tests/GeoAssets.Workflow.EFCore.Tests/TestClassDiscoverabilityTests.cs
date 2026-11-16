using System.Reflection;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Convention guard: xUnit 2.x only discovers <c>public</c> test classes, and silently ignores
/// any other class no matter how many <c>[Fact]</c>/<c>[Theory]</c> methods it has. A class whose
/// tests are all inherited from a GeoAssets.Workflow.TestKit contract-suite base is the one shape
/// neither the compiler nor an analyzer catches when it's made non-public (CA1515 actively
/// suggests it), which is how the whole <c>IServiceOrderRepository</c> contract suite went
/// unexecuted against <c>EFServiceOrderRepository</c>. See XD01-303.
/// </summary>
public sealed class TestClassDiscoverabilityTests
{
    [Fact]
    public void EveryConcreteClassWithTestMethods_IsVisibleToXunitDiscovery()
    {
        var undiscoverable = typeof(TestClassDiscoverabilityTests).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsVisible: false })
            .Where(type => type
                .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(method => method.GetCustomAttributes<FactAttribute>(inherit: true).Any()))
            .Select(type => type.FullName)
            .ToList();

        undiscoverable.Should().BeEmpty(
            "xUnit only discovers public test classes, so the [Fact]/[Theory] methods on these (declared or inherited from a contract-suite base) never run");
    }
}
