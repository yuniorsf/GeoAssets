using FluentAssertions;
using GeoAssets.Server.Tests.Architecture;
using Xunit;

namespace GeoAssets.Server.Tests;

[Trait("Category", "Architecture")]
public sealed class ServerArchitectureTests
{
    [Fact]
    public void ServerAssembly_ShouldNotReference_IpBillingVerticalAssemblies()
    {
        var referencedNames = typeof(GeoAssetsRestApiExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .ToArray();

        referencedNames.Should().NotContain(
            name => name.StartsWith("GeoAssets.IpBilling", StringComparison.Ordinal),
            because: "the Core Server (GeoAssets.Server) owns geographical features, alphanumeric attributes, and topological relations, and must not depend on the GEOS_FAT_IP vertical domain");
    }

    [Fact]
    public void ServerKernelTypes_ShouldNotReference_ProviderDomainOrVerticalNamespaces_OutsideAllowlist()
    {
        var result = EvaluateServerKernelRatchet();

        result.NewOffenders.Should().BeEmpty(
            because: "the GeoAssets.Server kernel must not depend on {0} (ADR-001 invariant I1); offending types and what they reference:{1}",
            string.Join(", ", KernelReferenceRatchet.ForbiddenNamespacePrefixes),
            DescribeViolations(result.NewOffenders));
    }

    [Fact]
    public void KernelReferenceAllowlist_ShouldOnlyShrink_EveryEntryMustStillViolate()
    {
        var result = EvaluateServerKernelRatchet();

        result.StaleAllowlistEntries.Should().BeEmpty(
            because: "the kernel reference allowlist is a ratchet (XD01-352): once a type stops referencing provider/domain/vertical namespaces its entry must be removed from KernelReferenceRatchet.Allowlist so the coupling cannot return");
    }

    [Theory]
    [InlineData("GeoAssets.Server.Modules", false)]
    [InlineData("GeoAssets.Server.Modules.NeutralOperator", false)]
    [InlineData("GeoAssets.Server", true)]
    [InlineData("GeoAssets.Server.Occupancy", true)]
    [InlineData("GeoAssets.Server.ModulesExtra", true)]
    [InlineData(null, true)]
    public void IsKernelNamespace_ExemptsOnlyTheModuleQuarantineNamespace(string? ns, bool expected) =>
        KernelReferenceRatchet.IsKernelNamespace(ns).Should().Be(expected);

    [Fact]
    public void IsKernelType_ServerKernelType_IsKernel() =>
        KernelReferenceRatchet.IsKernelType(typeof(GeoAssetsRestApiExtensions)).Should().BeTrue();

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> ScanServerAssembly() =>
        KernelReferenceScanner.FindForbiddenReferences(
            typeof(GeoAssetsRestApiExtensions).Assembly.GetTypes().Where(KernelReferenceRatchet.IsKernelType),
            KernelReferenceRatchet.IsForbidden);

    private static KernelRatchetResult EvaluateServerKernelRatchet() =>
        KernelReferenceRatchet.Evaluate(ScanServerAssembly(), KernelReferenceRatchet.Allowlist);

    private static string DescribeViolations(IEnumerable<string> owners)
    {
        var violations = ScanServerAssembly();
        return string.Concat(owners.Select(owner =>
            $"{Environment.NewLine}  {owner} -> {string.Join(", ", violations[owner])}"));
    }
}

