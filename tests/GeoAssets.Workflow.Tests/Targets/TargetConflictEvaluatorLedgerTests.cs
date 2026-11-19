using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Xunit;

namespace GeoAssets.Workflow.Tests.Targets;

/// <summary>
/// <see cref="TargetConflictEvaluator.Evaluate(TargetLedgerOrder, IReadOnlyCollection{string}, IReadOnlyCollection{OrderTargetUsage})"/>
/// (XD01-379): the ledger overload — orders of different verticals are never one family, whatever their ids.
/// </summary>
public sealed class TargetConflictEvaluatorLedgerTests
{
    private const string Feature = "feat-1";

    private static TargetLedgerOrder Order(OrderTargetExclusivity own, string vertical = "isp", string orderId = "ord-own") =>
        new(vertical, orderId, "type-1", Guid.NewGuid(), new OrderTargetPolicy { TargetAssetTypeIds = ["pole"], Exclusivity = own });

    private static OrderTargetUsage Usage(OrderTargetExclusivity exclusivity, string vertical, string orderId = "ord-other", string? parent = null) =>
        new(Feature, orderId, "type-2", string.Empty, parent, exclusivity, vertical);

    [Fact]
    public void Evaluate_NullOrder_Throws()
    {
        var act = () => TargetConflictEvaluator.Evaluate((TargetLedgerOrder)null!, [Feature], []);

        act.Should().Throw<ArgumentNullException>().WithParameterName("order");
    }

    [Fact]
    public void Evaluate_NullFeatureIdsOrUsages_Throws()
    {
        var order = Order(OrderTargetExclusivity.None);

        var featureIds = () => TargetConflictEvaluator.Evaluate(order, null!, []);
        var usages = () => TargetConflictEvaluator.Evaluate(order, [Feature], null!);

        featureIds.Should().Throw<ArgumentNullException>().WithParameterName("featureIds");
        usages.Should().Throw<ArgumentNullException>().WithParameterName("usages");
    }

    [Theory]
    [InlineData(OrderTargetExclusivity.None, OrderTargetExclusivity.None, OrderTargetConflictLevel.None)]
    [InlineData(OrderTargetExclusivity.WarnOnOverlap, OrderTargetExclusivity.None, OrderTargetConflictLevel.Warning)]
    [InlineData(OrderTargetExclusivity.None, OrderTargetExclusivity.WarnOnOverlap, OrderTargetConflictLevel.Warning)]
    [InlineData(OrderTargetExclusivity.BlockOnOverlap, OrderTargetExclusivity.None, OrderTargetConflictLevel.Blocked)]
    [InlineData(OrderTargetExclusivity.None, OrderTargetExclusivity.BlockOnOverlap, OrderTargetConflictLevel.Blocked)]
    [InlineData(OrderTargetExclusivity.WarnOnOverlap, OrderTargetExclusivity.BlockOnOverlap, OrderTargetConflictLevel.Blocked)]
    public void Evaluate_UsageOfAnotherVertical_CombinesTheStrongerExclusivity(
        OrderTargetExclusivity own, OrderTargetExclusivity other, OrderTargetConflictLevel expected)
    {
        var result = TargetConflictEvaluator.Evaluate(Order(own), [Feature], [Usage(other, "other-vertical")]);

        result[Feature].Should().Be(expected);
    }

    [Fact]
    public void Evaluate_UsageOfAnotherVerticalWithTheSameOrderId_StillCounts()
    {
        var result = TargetConflictEvaluator.Evaluate(
            Order(OrderTargetExclusivity.BlockOnOverlap, "isp", "ord-1"), [Feature], [Usage(OrderTargetExclusivity.None, "other-vertical", "ord-1")]);

        result[Feature].Should().Be(OrderTargetConflictLevel.Blocked, "a same-id order of another vertical is a different order");
    }

    [Fact]
    public void Evaluate_UsageOfTheOrderItself_IsIgnored()
    {
        var result = TargetConflictEvaluator.Evaluate(
            Order(OrderTargetExclusivity.BlockOnOverlap, "isp", "ord-1"), [Feature], [Usage(OrderTargetExclusivity.BlockOnOverlap, "isp", "ord-1")]);

        result[Feature].Should().Be(OrderTargetConflictLevel.None);
    }

    [Fact]
    public void Evaluate_UsageNamingTheOrderAsParent_IsFamilyOnlyWithinTheSameVertical()
    {
        var same = TargetConflictEvaluator.Evaluate(
            Order(OrderTargetExclusivity.BlockOnOverlap, "isp", "ord-1"), [Feature], [Usage(OrderTargetExclusivity.None, "isp", "ord-x", parent: "ord-1")]);
        var other = TargetConflictEvaluator.Evaluate(
            Order(OrderTargetExclusivity.BlockOnOverlap, "isp", "ord-1"), [Feature], [Usage(OrderTargetExclusivity.None, "other-vertical", "ord-x", parent: "ord-1")]);

        same[Feature].Should().Be(OrderTargetConflictLevel.None, "a child of the order in the same vertical is family, as in-process");
        other[Feature].Should().Be(OrderTargetConflictLevel.Blocked, "an order of another vertical is never family, whatever parent id it names");
    }

    [Fact]
    public void Evaluate_FeaturesWithoutUsages_AreNone_AndUsagesOfUnlistedFeaturesAreIgnored()
    {
        var unrelated = new OrderTargetUsage("feat-2", "ord-other", "t", string.Empty, null, OrderTargetExclusivity.BlockOnOverlap, "other-vertical");

        var result = TargetConflictEvaluator.Evaluate(Order(OrderTargetExclusivity.None), [Feature], [unrelated]);

        result.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, OrderTargetConflictLevel>(Feature, OrderTargetConflictLevel.None));
    }

    [Fact]
    public void Evaluate_InProcessOverload_RemoteUsageWithTheSameOrderId_IsNotTheOrderItself()
    {
        var order = new ServiceOrder { Id = "ord-1" };
        var usage = new OrderTargetUsageBuilder().WithFeatureId(Feature).WithOrderId("ord-1").WithExclusivity(OrderTargetExclusivity.BlockOnOverlap).Build() with { VerticalId = "isp" };

        var result = TargetConflictEvaluator.Evaluate(order, OrderTargetExclusivity.None, [Feature], [usage]);

        result[Feature].Should().Be(OrderTargetConflictLevel.Blocked);
    }

    [Fact]
    public void Evaluate_InProcessOverload_KernelUsageOfTheSameOrder_IsStillOwn()
    {
        var order = new ServiceOrder { Id = "ord-1" };
        var usage = new OrderTargetUsageBuilder().WithFeatureId(Feature).WithOrderId("ord-1").WithExclusivity(OrderTargetExclusivity.BlockOnOverlap).Build();

        TargetConflictEvaluator.Evaluate(order, OrderTargetExclusivity.None, [Feature], [usage])[Feature]
            .Should().Be(OrderTargetConflictLevel.None);
    }
}
