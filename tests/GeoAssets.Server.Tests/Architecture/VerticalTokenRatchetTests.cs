using System.Text.RegularExpressions;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Xunit;

namespace GeoAssets.Server.Tests.Architecture;

[Trait("Category", "Architecture")]
public sealed class VerticalTokenRatchetTests
{
    private const string PoleProbeKey = "GeoAssets.Workflow.Orders.PoleProbeFixture";

    public static TheoryData<string, string[]> TokenCases => new()
    {
        { "PoleCensus", ["Pole"] },
        { "Poles", ["Pole"] },
        { "Contractors", ["Contractor"] },
        { "NeutralOperatorModule", ["NeutralOperator"] },
        { "Dispatch", [] },
        { "Display", [] },
        { "Policy", [] },
        { "Polygon", [] },
        { "Neutral", [] },
        { "Operator", [] },
        { "Municipal", [] },
        { "ISPRoute", ["Isp"] },
        { "FTTH", ["Ftth"] },
        { "poleFeatureIds", ["Pole"] },
    };

    public static TheoryData<string?, bool> NamespaceCases => new()
    {
        { "GeoAssets.Workflow.Orders", true },
        { "GeoAssets.Workflow.Rules", true },
        { "GeoAssets.Workflow.Targets", true },
        { "GeoAssets.Workflow.Selection", true },
        { "GeoAssets.Workflow.Notifications", true },
        { "GeoAssets.Workflow.Selection.Strategies", true },
        { "GeoAssets.Workflow.OrdersExtra", false },
        { "GeoAssets.NeutralOperator.Application.Occupancy", false },
        { "GeoAssets.Workflow", false },
        { null, false },
    };

    [Theory]
    [MemberData(nameof(TokenCases))]
    public void FindTokens_MatchesWholeWordsOnly(string identifier, string[] expected)
    {
        VerticalTokenRatchet.FindTokens(identifier).Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(NamespaceCases))]
    public void IsGenericEngineNamespace_MatchesWholeSegments(string? ns, bool expected)
    {
        VerticalTokenRatchet.IsGenericEngineNamespace(ns).Should().Be(expected);
    }

    [Fact]
    public void FindViolations_NewPoleTypeInOrdersNamespace_IsReported()
    {
        var violations = VerticalTokenRatchet.FindViolations([typeof(PoleProbeFixture)]);

        violations.Should().ContainKey(PoleProbeKey);
        KernelReferenceRatchet.Evaluate(violations, VerticalTokenRatchet.Allowlist)
            .NewOffenders.Should().Contain(PoleProbeKey);
    }

    [Fact]
    public void FindViolations_CleanEngineType_ReportsNothing()
    {
        VerticalTokenRatchet.FindViolations([typeof(CleanEngineFixture)]).Should().BeEmpty();
    }

    [Fact]
    public void FindViolations_MemberAndParameterNames_AreReported()
    {
        var violations = VerticalTokenRatchet.FindViolations([typeof(MemberTokenFixture)]);

        violations.Should().ContainKey("GeoAssets.Workflow.Rules.MemberTokenFixture");
        var entries = violations["GeoAssets.Workflow.Rules.MemberTokenFixture"];
        entries.Should().Contain(e => e.Contains("manholeId", StringComparison.Ordinal));
        entries.Should().Contain(e => e.Contains("ContractorCount", StringComparison.Ordinal));
    }

    [Fact]
    public void EngineScope_ExcludesVerticalNamespaces()
    {
        var scoped = new[] { typeof(PoleProbeFixture), typeof(OutOfScopeOccupancyFixture) }
            .Where(t => VerticalTokenRatchet.IsGenericEngineNamespace(t.Namespace));

        var violations = VerticalTokenRatchet.FindViolations(scoped);

        violations.Keys.Should().BeEquivalentTo([PoleProbeKey]);
    }

    [Fact]
    public void Evaluate_AllowlistedOwnerWithoutViolation_IsStale()
    {
        // The real allowlist is empty (the ratchet reached zero with XD01-381), so the staleness rule is held to a synthetic one.
        const string owner = "GeoAssets.Workflow.Orders.SyntheticAllowlistedOwner";
        var allowlist = new Dictionary<string, string>(StringComparer.Ordinal) { [owner] = "XD01-350 (P6) — synthetic entry" };
        var empty = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        var result = KernelReferenceRatchet.Evaluate(empty, allowlist);

        result.StaleAllowlistEntries.Should().Contain(owner);
    }

    [Fact]
    public void FindDomainAssemblyReferences_EngineAssembly_ReportsNothing()
    {
        var references = VerticalTokenRatchet.FindDomainAssemblyReferences([typeof(OrderTypeRegistry).Assembly]);

        references.Should().BeEmpty();
    }

    [Fact]
    public void FindDomainAssemblyReferences_CleanAssembly_ReportsNothing()
    {
        VerticalTokenRatchet.FindDomainAssemblyReferences([typeof(GeoFeature).Assembly]).Should().BeEmpty();
    }

    [Fact]
    public void Allowlists_EveryEntryNamesTheEpicThatRemovesIt()
    {
        var pattern = new Regex(@"^XD01-3(4[4-9]|5[01]) \(P[0-7]\)", RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));

        // XD01-381 removed the last entries: the ratchet is at zero. A future entry must still name the epic that removes it.
        VerticalTokenRatchet.Allowlist.Should().BeEmpty("the vertical-token ratchet reached zero with XD01-381 — never add an entry back");
        VerticalTokenRatchet.DomainReferenceAllowlist.Should().BeEmpty("the engine's last GeoAssets.Domain.* reference left with XD01-381 — never add one back");
        VerticalTokenRatchet.Allowlist.Values.Where(v => !pattern.IsMatch(v)).Should().BeEmpty("every entry names the epic that removes it");
        VerticalTokenRatchet.DomainReferenceAllowlist.Values.Where(v => !pattern.IsMatch(v)).Should().BeEmpty("every entry names the epic that removes it");
    }
}
