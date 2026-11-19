using System.Reflection;
using System.Text.RegularExpressions;
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
    public void KernelServiceOrderDbContext_RemainingVerticalTokenViolations_AreOnlyXD01381s_ContractorUs_Paving_ManholeAudit()
    {
        var violations = VerticalTokenRatchet.FindViolations([typeof(ServiceOrderDbContext)]);

        var hits = violations.Should().ContainKey("GeoAssets.Workflow.Persistence.ServiceOrderDbContext").WhoseValue;
        var remaining = new Regex(@"^.+ ~ (Contractor|Paving|Manhole)$", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        hits.Should().NotBeEmpty().And.OnlyContain(hit => remaining.IsMatch(hit),
            "only XD01-381's paving, manhole-audit and contractor-US DbSets are left in the kernel context. Hits: {0}", string.Join("; ", hits));
        hits.Should().NotContain(hit => hit.Contains("Occupancy", StringComparison.Ordinal) || hit.Contains("Pole", StringComparison.Ordinal));
        VerticalTokenRatchet.Allowlist["GeoAssets.Workflow.Persistence.ServiceOrderDbContext"].Should().Contain("XD01-381").And.NotContain("XD01-380");
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
    public void KernelPersistenceTypes_ReferencingTheNeutralOperatorNamespace_AreOnlyTheSharedUseDispatchOnes()
    {
        var offenders = KernelReferenceScanner.FindForbiddenReferences(
            KernelPersistence.GetTypes(),
            type => type.Namespace is { } ns
                && (string.Equals(ns, NeutralOperatorNamespace, StringComparison.Ordinal)
                    || ns.StartsWith(NeutralOperatorNamespace + ".", StringComparison.Ordinal)));

        // Shared-use dispatch (XD01-316) persistence is not part of XD01-380: it stays in the kernel persistence assembly.
        offenders.Keys.Should().BeEquivalentTo(
            [
                "GeoAssets.Workflow.WorkflowEFCoreServiceExtensions",
                "GeoAssets.Workflow.Persistence.ActivitySharedUseFanOutTracer",
                "GeoAssets.Workflow.Persistence.ObservableSharedUseDispatchService",
            ],
            "only AddSharedUseDispatchPersistence and its two decorators still name the vertical. Offenders: {0}",
            string.Join("; ", offenders.Select(o => $"{o.Key}: {string.Join(", ", o.Value)}")));
        offenders.Values.SelectMany(references => references).Should().OnlyContain(
            reference => reference.StartsWith("GeoAssets.NeutralOperator.Application.SharedUse.", StringComparison.Ordinal));
    }
}
