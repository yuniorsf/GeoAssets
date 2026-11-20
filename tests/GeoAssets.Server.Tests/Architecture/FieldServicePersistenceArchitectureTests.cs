using System.Reflection;
using FluentAssertions;
using GeoAssets.FieldService.Persistence;
using GeoAssets.Workflow.Persistence;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// XD01-381 (ADR-001 Phase 6, the ratchet's last step): the contractor US ledger, US catalog, postings, paving measurements and manhole
/// audits — their records, configurations, mappers, stores and the inspection commit — left the kernel's
/// <c>GeoAssets.Workflow.EFCore</c> for the FieldService vertical's own persistence assembly, and stay there. Both vertical-token
/// allowlists are now empty; this pins that and what the move removed.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class FieldServicePersistenceArchitectureTests
{
    private static readonly Assembly KernelPersistence = typeof(ServiceOrderDbContext).Assembly;
    private static readonly Assembly FieldServicePersistence = typeof(FieldServiceDbContext).Assembly;

    private static readonly string[] MovedTypes =
    [
        "GeoAssets.Workflow.Persistence.Configurations.ContractorUsLedgerEntryRecordConfiguration",
        "GeoAssets.Workflow.Persistence.Configurations.ContractorUsPostingRecordConfiguration",
        "GeoAssets.Workflow.Persistence.Configurations.ManholeAuditRecordConfiguration",
        "GeoAssets.Workflow.Persistence.Configurations.PavingMeasurementRecordConfiguration",
        "GeoAssets.Workflow.Persistence.Configurations.UsCatalogItemRecordConfiguration",
        "GeoAssets.Workflow.Persistence.ContractorUsLedgerMapper",
        "GeoAssets.Workflow.Persistence.ContractorUsPostingMapper",
        "GeoAssets.Workflow.Persistence.EFContractorUsLedgerRepository",
        "GeoAssets.Workflow.Persistence.EFContractorUsPostingRepository",
        "GeoAssets.Workflow.Persistence.EFManholeAuditRepository",
        "GeoAssets.Workflow.Persistence.EFPavingMeasurementRepository",
        "GeoAssets.Workflow.Persistence.EFUsCatalogRepository",
        "GeoAssets.Workflow.Persistence.Entities.ContractorUsLedgerEntryRecord",
        "GeoAssets.Workflow.Persistence.Entities.ContractorUsPostingRecord",
        "GeoAssets.Workflow.Persistence.Entities.ManholeAuditRecord",
        "GeoAssets.Workflow.Persistence.Entities.PavingMeasurementRecord",
        "GeoAssets.Workflow.Persistence.Entities.UsCatalogItemRecord",
        "GeoAssets.Workflow.Persistence.InspectionCommitResult",
        "GeoAssets.Workflow.Persistence.InspectionCommitService",
        "GeoAssets.Workflow.Persistence.ManholeAuditMapper",
        "GeoAssets.Workflow.Persistence.PavingMeasurementMapper",
        "GeoAssets.Workflow.Persistence.UsCatalogItemMapper",
    ];

    private static readonly string[] VerticalNameParts = ["Contractor", "Paving", "Manhole", "UsCatalog", "InspectionCommit"];

    [Fact]
    public void VerticalTokenAllowlists_AreBothEmpty_TheRatchetReachedZero()
    {
        VerticalTokenRatchet.Allowlist.Should().BeEmpty("no engine type names a vertical domain any more");
        VerticalTokenRatchet.DomainReferenceAllowlist.Should().BeEmpty("no engine assembly references GeoAssets.Domain.* any more");
    }

    [Fact]
    public void KernelPersistenceAssembly_DeclaresNoContractorPavingManholeUsCatalogOrInspectionCommitType_AndNoneOfTheMovedOnes()
    {
        var typeNames = KernelPersistence.GetTypes().Select(t => t.FullName ?? t.Name).ToList();

        typeNames.Should().NotIntersectWith(MovedTypes);
        typeNames.Should().NotContain(name => VerticalNameParts.Any(part => name.Contains(part, StringComparison.Ordinal)),
            "the contractor-US, paving, manhole-audit and inspection-commit types live in the FieldService vertical's persistence assembly");
    }

    [Fact]
    public void MovedTypes_AreDeclaredByTheFieldServicePersistenceAssembly_UnderItsOwnNamespace()
    {
        var declared = FieldServicePersistence.GetTypes().Select(t => t.Name).ToHashSet(StringComparer.Ordinal);

        declared.Should().IntersectWith(MovedTypes.Select(name => name[(name.LastIndexOf('.') + 1)..]));
        FieldServicePersistence.GetTypes()
            .Where(t => t.Namespace is not null && !t.Name.StartsWith('<'))
            .Select(t => t.Namespace!)
            .Should().OnlyContain(ns => ns.StartsWith("GeoAssets.FieldService.Persistence", StringComparison.Ordinal));
    }

    [Fact]
    public void KernelServiceOrderDbContext_HasNoVerticalTokenViolation_ForPavingManholeOrContractor()
    {
        var violations = VerticalTokenRatchet.FindViolations(KernelPersistence.GetTypes());

        var hits = violations.Values.SelectMany(set => set).Where(hit =>
            hit.EndsWith("~ Paving", StringComparison.Ordinal)
            || hit.EndsWith("~ Manhole", StringComparison.Ordinal)
            || hit.EndsWith("~ Contractor", StringComparison.Ordinal)).ToList();

        hits.Should().BeEmpty("the vertical-token scan of GeoAssets.Workflow.EFCore finds no Paving, Manhole or Contractor identifier. Hits: {0}", string.Join("; ", hits));
        violations.Should().BeEmpty("with XD01-381 the engine persistence names no vertical domain at all");
    }

    [Fact]
    public void KernelPersistenceAssembly_DoesNotReferenceTheSanitationDomain_NorAnyFieldServiceAssembly()
    {
        var referenced = KernelPersistence.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        referenced.Should().NotContain("GeoAssets.Domain.Sanitation", "the paving spawn that read TrenchDimensions moved with the inspection commit");
        referenced.Should().NotContain(name => name.StartsWith("GeoAssets.Domain", StringComparison.Ordinal));
        referenced.Should().NotContain(name => name.StartsWith("GeoAssets.FieldService", StringComparison.Ordinal),
            "the reference runs vertical -> engine, never the reverse");
    }

    [Fact]
    public void KernelPersistenceTypes_NameNoFieldServiceNamespace()
    {
        var offenders = KernelReferenceScanner.FindForbiddenReferences(
            KernelPersistence.GetTypes(),
            type => type.Namespace is { } ns && ns.StartsWith("GeoAssets.FieldService", StringComparison.Ordinal));

        offenders.Should().BeEmpty("the engine's persistence knows no FieldService type. Offenders: {0}",
            string.Join("; ", offenders.Select(o => $"{o.Key}: {string.Join(", ", o.Value)}")));
    }

    [Fact]
    public void ServerHostAssembly_DoesNotReferenceTheFieldServicePersistenceAssembly()
    {
        var referenced = typeof(HttpTenantContext).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        referenced.Should().NotContain(FieldServicePersistence.GetName().Name,
            "the kernel host is domain-agnostic: the FieldService persistence is a vertical's, hosted by GeoAssets.FieldService.Server");
    }
}
