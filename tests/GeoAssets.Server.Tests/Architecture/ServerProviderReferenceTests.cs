using FluentAssertions;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// XD01-374 (ADR-001 Phase 4): no <c>GeoAssets.Server</c> type — kernel or module — names a <c>GeoAssets.Provider.*</c> namespace
/// except the PostgreSQL spatial module, the one place the host wires its provider.
/// </summary>
public sealed class ServerProviderReferenceTests
{
    private const string PostgresSpatialModuleNamespace = "GeoAssets.Server.Modules.PostgresSpatial";

    [Fact]
    public void ProviderReferences_OnlyInsideThePostgresSpatialModule()
    {
        var violations = KernelReferenceScanner.FindForbiddenReferences(
            typeof(GeoAssetsRestApiExtensions).Assembly.GetTypes(),
            type => type.Namespace is { } ns
                && (string.Equals(ns, "GeoAssets.Provider", StringComparison.Ordinal)
                    || ns.StartsWith("GeoAssets.Provider.", StringComparison.Ordinal)));

        var typesByOwner = typeof(GeoAssetsRestApiExtensions).Assembly.GetTypes()
            .GroupBy(KernelReferenceScanner.OwnerOf, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var offenders = violations.Keys
            .Where(owner => !string.Equals(typesByOwner[owner].Namespace, PostgresSpatialModuleNamespace, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        violations.Should().NotBeEmpty("the scan must see the PostgreSQL spatial module's provider references");
        offenders.Should().BeEmpty(
            "only {0} may reference GeoAssets.Provider.*; offenders: {1}",
            PostgresSpatialModuleNamespace,
            string.Join(", ", offenders.Select(o => $"{o} -> {string.Join("|", violations[o])}")));
    }
}
