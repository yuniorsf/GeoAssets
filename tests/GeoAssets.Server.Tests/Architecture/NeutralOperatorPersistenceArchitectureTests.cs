using System.Reflection;
using FluentAssertions;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Persistence;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// XD01-380 (ADR-001 Phase 6): the pole occupancy tables, stores and registration left the kernel's
/// <c>GeoAssets.Workflow.EFCore</c> for the NeutralOperator vertical's own persistence assembly — and stay there. The
/// vertical-token ratchet itself (<see cref="WorkflowEngineArchitectureTests"/>) does the shrink-only bookkeeping; this pins
/// what that change removed and what is left of it.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class NeutralOperatorPersistenceArchitectureTests
{
    private const string NeutralOperatorNamespace = "GeoAssets.NeutralOperator";

    private static readonly Assembly KernelPersistence = typeof(ServiceOrderDbContext).Assembly;

    private static readonly string[] MovedTypes =
    [
        "GeoAssets.Workflow.Persistence.Configurations.OccupancyChangeSetRecordConfiguration",
        "GeoAssets.Workflow.Persistence.Configurations.PoleOccupancyEntryRecordConfiguration",
        "GeoAssets.Workflow.Persistence.Configurations.PoleOccupancyRecordConfiguration",
        "GeoAssets.Workflow.Persistence.EFOccupancyChangeSetRepository",
        "GeoAssets.Workflow.Persistence.EFPoleOccupancyRepository",
        "GeoAssets.Workflow.Persistence.Entities.OccupancyChangeSetRecord",
        "GeoAssets.Workflow.Persistence.Entities.PoleOccupancyEntryRecord",
        "GeoAssets.Workflow.Persistence.Entities.PoleOccupancyRecord",
        "GeoAssets.Workflow.Persistence.ObservableOccupancyCommitService",
        "GeoAssets.Workflow.Persistence.OccupancyChangeSetMapper",
        "GeoAssets.Workflow.Persistence.PoleOccupancyMapper",
    ];

    [Fact]
    public void VerticalTokenAllowlist_HoldsNoEntryOfTheOccupancyMove()
    {
        var allowlist = VerticalTokenRatchet.Allowlist;

        allowlist.Keys.Should().NotIntersectWith(MovedTypes, "the moved types no longer violate, so their entries are gone");
        allowlist.Keys.Should().NotContain("GeoAssets.Workflow.WorkflowEFCoreServiceExtensions");
        allowlist.Values.Should().NotContain(value => value.Contains("XD01-380 moves", StringComparison.Ordinal),
            "no entry waits for XD01-380 any more");
        allowlist.Keys.Should().NotContain(key => key.Contains("Occupancy", StringComparison.Ordinal),
            "no occupancy type is left in the kernel persistence to be allowlisted");
    }

    [Fact]
    public void KernelPersistenceAssembly_DeclaresNoOccupancyType_AndNoneOfTheMovedOnes()
    {
        var typeNames = KernelPersistence.GetTypes().Select(t => t.FullName ?? t.Name).ToList();

        typeNames.Should().NotIntersectWith(MovedTypes);
        typeNames.Should().NotContain(name => name.Contains("Occupancy", StringComparison.Ordinal) || name.Contains("PoleOccupan", StringComparison.Ordinal));
    }

    [Fact]
    public void KernelServiceOrderDbContext_HasNoVerticalTokenViolation_AfterXD01381MovedTheLastVerticalDbSets()
    {
        var violations = VerticalTokenRatchet.FindViolations([typeof(ServiceOrderDbContext)]);

        violations.Should().NotContainKey("GeoAssets.Workflow.Persistence.ServiceOrderDbContext",
            "XD01-380 took the occupancy tables and XD01-381 the paving, manhole-audit and contractor-US ones: no vertical DbSet is left in the kernel context");
        VerticalTokenRatchet.Allowlist.Should().NotContainKey("GeoAssets.Workflow.Persistence.ServiceOrderDbContext");
    }

    [Fact]
    public void KernelPersistenceAssembly_DoesNotReferenceTheVerticalsPersistenceAssembly()
    {
        var referenced = KernelPersistence.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        referenced.Should().NotContain(typeof(NeutralOperatorDbContext).Assembly.GetName().Name,
            "the reference runs vertical -> engine, never the reverse");
        KernelPersistence.GetTypes().Where(t => t.Namespace is not null && t.Namespace.StartsWith("GeoAssets.Workflow.Persistence", StringComparison.Ordinal))
            .Should().NotBeEmpty();
    }

    [Fact]
    public void KernelPersistenceTypes_NameNoOccupancyNamespaceOfTheVertical()
    {
        var offenders = KernelReferenceScanner.FindForbiddenReferences(
            KernelPersistence.GetTypes(),
            type => type.Namespace is { } ns
                && ns.StartsWith(NeutralOperatorNamespace + ".", StringComparison.Ordinal)
                && ns.Contains("Occupancy", StringComparison.Ordinal));

        offenders.Should().BeEmpty("the occupancy stores, ports and records left with XD01-380. Offenders: {0}",
            string.Join("; ", offenders.Select(o => $"{o.Key}: {string.Join(", ", o.Value)}")));
    }

    [Fact]
    public void KernelPersistenceTypes_NameNoNeutralOperatorNamespace()
    {
        var offenders = KernelReferenceScanner.FindForbiddenReferences(
            KernelPersistence.GetTypes(),
            type => type.Namespace is { } ns
                && (string.Equals(ns, NeutralOperatorNamespace, StringComparison.Ordinal)
                    || ns.StartsWith(NeutralOperatorNamespace + ".", StringComparison.Ordinal)));

        // XD01-388 moved AddSharedUseDispatchPersistence and the shared-use dispatch (XD01-316) decorators to the vertical's persistence.
        offenders.Should().BeEmpty("no kernel persistence type names the vertical. Offenders: {0}",
            string.Join("; ", offenders.Select(o => $"{o.Key}: {string.Join(", ", o.Value)}")));
    }
}
