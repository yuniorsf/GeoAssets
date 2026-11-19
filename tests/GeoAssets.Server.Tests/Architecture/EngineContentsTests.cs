using System.Reflection;
using FluentAssertions;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>XD01-376: the generic engine holds only generic namespaces and references no vertical assembly; SlaGuard is an engine add-on.</summary>
[Trait("Category", "Architecture")]
public sealed class EngineContentsTests
{
    private static readonly string[] AllowedSubNamespaces =
        ["Orders", "Rules", "Targets", "Selection", "Notifications", "Dispatch", "Registry"];

    private static readonly string[] ForbiddenReferencePrefixes =
        ["GeoAssets.Domain.", "GeoAssets.NeutralOperator.", "GeoAssets.FieldService.", "GeoAssets.Integrations.", "GeoAssets.IpBilling"];

    private static Assembly EngineAssembly => typeof(OrderTypeRegistry).Assembly;

    private static IEnumerable<string> GeoAssetsReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("GeoAssets.", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    [Fact]
    public void Engine_ContainsOnlyGenericNamespaces()
    {
        var offenders = EngineAssembly.GetTypes()
            .Where(type => type.Namespace is not null && !IsGenericNamespace(type.Namespace))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.Should().BeEmpty("the engine holds only generic namespaces (XD01-376); offenders: {0}", string.Join(", ", offenders));
    }

    [Fact]
    public void Engine_ReferencesNoDomainAssembly()
    {
        var offenders = GeoAssetsReferences(EngineAssembly)
            .Where(name => ForbiddenReferencePrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        offenders.Should().BeEmpty("the engine must not reference vertical or domain assemblies (XD01-376); offenders: {0}", string.Join(", ", offenders));
    }

    [Fact]
    public void SlaGuard_IsAnEngineAddOn()
    {
        GeoAssetsReferences(typeof(SlaGuardServiceExtensions).Assembly).Should().BeSubsetOf(["GeoAssets.Workflow", "GeoAssets.Core"]);
    }

    private static bool IsGenericNamespace(string? ns) =>
        ns is not null
        && (string.Equals(ns, "GeoAssets.Workflow", StringComparison.Ordinal)
            || AllowedSubNamespaces.Any(sub =>
                string.Equals(ns, "GeoAssets.Workflow." + sub, StringComparison.Ordinal)
                || ns.StartsWith("GeoAssets.Workflow." + sub + ".", StringComparison.Ordinal)));
}
