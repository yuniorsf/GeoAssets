using FluentAssertions;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.Server.Modules.NeutralOperator;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// The NeutralOperator vertical left the kernel (XD01-382, AC3/AC5): no kernel type — nothing of <c>GeoAssets.Server</c> outside its
/// <c>GeoAssets.Server.Modules</c> quarantine — names any <c>GeoAssets.NeutralOperator*</c> namespace (the module library included), and
/// the kernel reference ratchet holds no entry at all since XD01-385 (SlaGuard left <c>Program</c>).
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
    public void KernelReferenceAllowlist_IsEmpty_AndNoKernelTypeViolates()
    {
        KernelReferenceRatchet.Allowlist.Should().BeEmpty();
        Violations().Should().BeEmpty("no kernel type names a forbidden namespace any more (XD01-385)");
    }
}
