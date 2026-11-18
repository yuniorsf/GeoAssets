using System.Text.RegularExpressions;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Occupancy;
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
        { "GeoAssets.Workflow.Occupancy", false },
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
        var owner = VerticalTokenRatchet.Allowlist.Keys.First();
        var empty = new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        var result = KernelReferenceRatchet.Evaluate(empty, VerticalTokenRatchet.Allowlist);

        result.StaleAllowlistEntries.Should().Contain(owner);
    }

    [Fact]
    public void FindDomainAssemblyReferences_ReportsWorkflowToDomainSanitation()
    {
        var references = VerticalTokenRatchet.FindDomainAssemblyReferences([typeof(OrderTypeRegistry).Assembly]);

        references.Should().ContainKey("GeoAssets.Workflow -> GeoAssets.Domain.Sanitation");
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

        VerticalTokenRatchet.Allowlist.Should().NotBeEmpty();
        VerticalTokenRatchet.DomainReferenceAllowlist.Should().NotBeEmpty();
        VerticalTokenRatchet.Allowlist.Values.Should().OnlyContain(v => pattern.IsMatch(v));
        VerticalTokenRatchet.DomainReferenceAllowlist.Values.Should().OnlyContain(v => pattern.IsMatch(v));
    }
}
