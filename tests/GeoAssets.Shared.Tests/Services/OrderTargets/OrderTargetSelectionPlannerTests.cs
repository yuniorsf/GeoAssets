using FluentAssertions;
using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Workflow.Selection.Strategies;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>The map selection as a selection-strategy run (XD01-312).</summary>
public sealed class OrderTargetSelectionPlannerTests
{
    private static readonly OrderTargetPolicy _policy = new() { TargetAssetTypeIds = ["pole", "manhole"] };

    private static readonly double[][][] _fence = [[[-70, 18], [-69, 18], [-69, 19], [-70, 18]]];

    private static readonly Dictionary<string, string> _loaded = new(StringComparer.Ordinal)
    {
        ["p2"] = "pole", ["p1"] = "pole", ["c1"] = "cable", ["m1"] = "manhole",
    };

    private static string? AssetTypeOf(string id) => _loaded.GetValueOrDefault(id);

    [Fact]
    public void NullArguments_Throw()
    {
        FluentActions.Invoking(() => OrderTargetSelectionPlanner.Plan(null!, ["p1"], null, AssetTypeOf)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => OrderTargetSelectionPlanner.Plan(_policy, null!, null, AssetTypeOf)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => OrderTargetSelectionPlanner.Plan(_policy, ["p1"], null, null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EmptySelection_IsAProblem_WithOrWithoutAFence(bool withFence)
    {
        var result = OrderTargetSelectionPlanner.Plan(_policy, [], withFence ? _fence : null, AssetTypeOf);

        result.Plan.Should().BeNull();
        result.Problem.Should().Be(OrderTargetSelectionPlanProblem.EmptySelection);
    }

    [Fact]
    public void SelectionMadeByOneFence_SendsThatFence_RestrictedToThePolicysAssetTypes()
    {
        var plan = OrderTargetSelectionPlanner.Plan(_policy, ["p1", "c1"], _fence, AssetTypeOf).Plan!;

        plan.StrategyId.Should().Be(PolygonFenceSelectionStrategy.Id);
        plan.IsFence.Should().BeTrue();
        plan.IdentifierCount.Should().Be(0);
        plan.Parameters.Keys.Should().BeEquivalentTo(PolygonFenceSelectionStrategy.RingsParameter, PolygonFenceSelectionStrategy.AssetTypeIdsParameter);
        plan.Parameters[PolygonFenceSelectionStrategy.RingsParameter].Should().BeSameAs(_fence);
        ((string[])plan.Parameters[PolygonFenceSelectionStrategy.AssetTypeIdsParameter]).Should().Equal("pole", "manhole");
    }

    [Fact]
    public void SelectionNotMadeByOneFence_SendsTheEligibleAndUnloadedIds_InOrdinalOrder_OnTheFeatureIdKey()
    {
        var plan = OrderTargetSelectionPlanner.Plan(_policy, ["p2", "c1", "zz-unloaded", "m1", "p1"], null, AssetTypeOf).Plan!;

        plan.StrategyId.Should().Be(IdentifierListSelectionStrategy.Id);
        plan.IsFence.Should().BeFalse();
        plan.IdentifierCount.Should().Be(4);
        plan.Parameters[IdentifierListSelectionStrategy.AttributeKeyParameter].Should().Be(IdentifierListSelectionStrategy.FeatureIdKey);
        ((string[])plan.Parameters[IdentifierListSelectionStrategy.ValuesParameter]).Should().Equal("m1", "p1", "p2", "zz-unloaded");
        ((string[])plan.Parameters[IdentifierListSelectionStrategy.AssetTypeIdsParameter]).Should().Equal("pole", "manhole");
    }

    [Fact]
    public void OnlyFeaturesOfOtherTypes_IsAProblem()
    {
        var result = OrderTargetSelectionPlanner.Plan(_policy, ["c1"], null, AssetTypeOf);

        result.Problem.Should().Be(OrderTargetSelectionPlanProblem.NoEligibleFeature);
    }

    [Fact]
    public void IdentifierListAtTheStrategysCeiling_IsPlanned_OneMoreIsAProblem()
    {
        var atCeiling = Enumerable.Range(0, IdentifierListSelectionStrategy.MaxDistinctValues).Select(i => $"u{i}").ToList();

        OrderTargetSelectionPlanner.Plan(_policy, atCeiling, null, AssetTypeOf).Plan!.IdentifierCount
            .Should().Be(IdentifierListSelectionStrategy.MaxDistinctValues);
        OrderTargetSelectionPlanner.Plan(_policy, [.. atCeiling, "one-more"], null, AssetTypeOf).Problem
            .Should().Be(OrderTargetSelectionPlanProblem.TooManyIdentifiers);
    }

    [Fact]
    public void ALargeSelectionMadeByOneFence_IsNotBoundByTheIdentifierCeiling()
    {
        var many = Enumerable.Range(0, IdentifierListSelectionStrategy.MaxDistinctValues + 1).Select(i => $"u{i}").ToList();

        OrderTargetSelectionPlanner.Plan(_policy, many, _fence, AssetTypeOf).Plan!.IsFence.Should().BeTrue();
    }

    [Fact]
    public void PlanResult_Planned_RejectsANullPlan() =>
        FluentActions.Invoking(() => OrderTargetSelectionPlanResult.Planned(null!)).Should().Throw<ArgumentNullException>();
}
