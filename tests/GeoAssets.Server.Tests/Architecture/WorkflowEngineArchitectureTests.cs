using System.Reflection;
using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

/// <summary>
/// XD01-353: the generic workflow engine (<c>Orders</c>, <c>Rules</c>, <c>Targets</c>, <c>Selection</c>,
/// <c>Notifications</c> in <c>GeoAssets.Workflow</c>) and <c>GeoAssets.Workflow.EFCore</c> stay vertical-agnostic
/// (ADR-001 invariant I2), with shrink-only allowlists for today's offenders.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class WorkflowEngineArchitectureTests
{
    private static readonly Assembly[] EngineAssemblies =
    [
        typeof(OrderTypeRegistry).Assembly,
        typeof(ServiceOrderDbContext).Assembly,
    ];

    [Fact]
    public void GenericEngineAndWorkflowEFCore_ShouldNotNameVerticalDomains_OutsideAllowlist()
    {
        var violations = ScanEngineTypes();
        var result = KernelReferenceRatchet.Evaluate(violations, VerticalTokenRatchet.Allowlist);

        result.NewOffenders.Should().BeEmpty(
            because: "the generic workflow engine must not name vertical domains ({0}) (ADR-001 invariant I2); offending types and identifiers:{1}",
            string.Join(", ", VerticalTokenRatchet.VerticalTokens),
            Describe(violations, result.NewOffenders));
    }

    [Fact]
    public void VerticalTokenAllowlist_ShouldOnlyShrink_EveryEntryMustStillViolate()
    {
        var result = KernelReferenceRatchet.Evaluate(ScanEngineTypes(), VerticalTokenRatchet.Allowlist);

        result.StaleAllowlistEntries.Should().BeEmpty(
            because: "the vertical-token allowlist is a ratchet (XD01-353): once a type stops naming a vertical domain its entry must be removed from VerticalTokenRatchet.Allowlist; stale entries: {0}",
            string.Join(", ", result.StaleAllowlistEntries));
    }

    [Fact]
    public void GenericEngineAssemblies_ShouldNotReference_DomainAssemblies_OutsideAllowlist()
    {
        var references = VerticalTokenRatchet.FindDomainAssemblyReferences(EngineAssemblies);
        var result = KernelReferenceRatchet.Evaluate(references, VerticalTokenRatchet.DomainReferenceAllowlist);

        result.NewOffenders.Should().BeEmpty(
            because: "generic engine assemblies must not reference GeoAssets.Domain.* (ADR-001 smell K); offending references: {0}",
            string.Join(", ", result.NewOffenders));
    }

    [Fact]
    public void DomainReferenceAllowlist_ShouldOnlyShrink_EveryEntryMustStillExist()
    {
        var references = VerticalTokenRatchet.FindDomainAssemblyReferences(EngineAssemblies);
        var result = KernelReferenceRatchet.Evaluate(references, VerticalTokenRatchet.DomainReferenceAllowlist);

        result.StaleAllowlistEntries.Should().BeEmpty(
            because: "the engine → GeoAssets.Domain.* allowlist is a ratchet (XD01-353): remove an entry once its reference is gone; stale entries: {0}",
            string.Join(", ", result.StaleAllowlistEntries));
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> ScanEngineTypes() =>
        VerticalTokenRatchet.FindViolations(
            typeof(OrderTypeRegistry).Assembly.GetTypes()
                .Where(t => VerticalTokenRatchet.IsGenericEngineNamespace(t.Namespace))
                .Concat(typeof(ServiceOrderDbContext).Assembly.GetTypes()));

    private static string Describe(IReadOnlyDictionary<string, IReadOnlySet<string>> violations, IEnumerable<string> owners) =>
        string.Concat(owners.Select(owner =>
            $"{Environment.NewLine}  {owner} -> {string.Join("; ", violations[owner])}"));
}
