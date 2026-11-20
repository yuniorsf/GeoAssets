using FluentAssertions;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.Server.Modules.NeutralOperator;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// The NeutralOperator vertical left the kernel (XD01-382, AC3/AC5): no kernel type — nothing of <c>GeoAssets.Server</c> outside its
/// <c>GeoAssets.Server.Modules</c> quarantine — names any <c>GeoAssets.NeutralOperator*</c> namespace (the module library included), and
/// the kernel reference ratchet holds no entry on the vertical's account any more: only <c>Program</c> remains, for SlaGuard.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class NeutralOperatorExtractionArchitectureTests
{
    private const string NeutralOperatorNamespace = "GeoAssets.NeutralOperator";

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> Violations() =>
        KernelReferenceScanner.FindForbiddenReferences(
            typeof(GeoAssetsRestApiExtensions).Assembly.GetTypes().Where(KernelReferenceRatchet.IsKernelType),
            KernelReferenceRatchet.IsForbidden);

    [Fact]
    public void ForbiddenPrefixes_CoverEveryNeutralOperatorNamespace_TheModuleLibraryIncluded()
    {
        KernelReferenceRatchet.IsForbidden(typeof(NeutralOperatorModule)).Should().BeTrue("GeoAssets.NeutralOperator.Modules is the vertical's");
        KernelReferenceRatchet.IsForbidden(typeof(GeoAssets.NeutralOperator.Domain.GisExport.GisExportProjector)).Should().BeTrue();
        KernelReferenceRatchet.IsForbidden(typeof(GeoAssets.NeutralOperator.Application.SharedUse.SharedUseOrderTypes)).Should().BeTrue();
        KernelReferenceRatchet.IsForbidden(typeof(GeoAssets.NeutralOperator.Persistence.NeutralOperatorDbContext)).Should().BeTrue();
        KernelReferenceRatchet.ForbiddenNamespacePrefixes.Should().Contain(NeutralOperatorNamespace);
    }

    [Fact]
    public void KernelHostedModule_IsInTheQuarantineNamespace_SoItIsNotAKernelType()
    {
        KernelReferenceRatchet.IsKernelType(typeof(KernelHostedNeutralOperatorModule)).Should().BeFalse();
        typeof(KernelHostedNeutralOperatorModule).Namespace.Should().StartWith(KernelReferenceRatchet.ModuleQuarantineNamespace + ".");
    }

    [Fact]
    public void NoKernelType_ReferencesAnyNeutralOperatorNamespace()
    {
        var offenders = Violations()
            .Where(owner => owner.Value.Any(reference => reference.StartsWith(NeutralOperatorNamespace + ".", StringComparison.Ordinal)))
            .Select(owner => $"{owner.Key} -> {string.Join(", ", owner.Value.Where(r => r.StartsWith(NeutralOperatorNamespace + ".", StringComparison.Ordinal)))}")
            .ToList();

        offenders.Should().BeEmpty("the kernel carries no NeutralOperator type or call (ADR-001 invariant I1); the vertical is reached through KernelHostedNeutralOperatorModule only");
    }

    [Fact]
    public void KernelReferenceAllowlist_HoldsOnlyProgram_ForSlaGuard_AndNothingOnTheVerticalsAccount()
    {
        KernelReferenceRatchet.Allowlist.Keys.Should().Equal(["Program"]);
        KernelReferenceRatchet.Allowlist.Should().NotContain(
            entry => entry.Key.Contains("NeutralOperator", StringComparison.OrdinalIgnoreCase)
                || entry.Value.Contains("NeutralOperator", StringComparison.OrdinalIgnoreCase)
                || entry.Key.Contains("GisExport", StringComparison.OrdinalIgnoreCase)
                || entry.Value.Contains("GIS export", StringComparison.OrdinalIgnoreCase));
        Violations().Keys.Should().Equal(["Program"], "the only kernel type that still names a forbidden namespace is Program");
        Violations()["Program"].Should().OnlyContain(
            reference => reference.StartsWith("GeoAssets.Workflow.SlaGuard.", StringComparison.Ordinal),
            "what Program still references is the SlaGuard engine, not the vertical");
    }
}
