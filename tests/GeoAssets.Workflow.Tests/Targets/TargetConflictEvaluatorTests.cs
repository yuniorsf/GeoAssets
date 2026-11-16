using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Xunit;

namespace GeoAssets.Workflow.Tests.Targets;

public class TargetConflictEvaluatorTests
{
    private const string OrderId = "ord-own";
    private const string Feature = "feat-1";

    private static ServiceOrder Order(string? parentOrderId = null, params string[] childOrderIds)
    {
        var order = new ServiceOrder { Id = OrderId, ParentOrderId = parentOrderId };
        order.ChildOrderIds.AddRange(childOrderIds);
        return order;
    }

    private static OrderTargetUsageBuilder Usage(OrderTargetExclusivity exclusivity = OrderTargetExclusivity.None) =>
        new OrderTargetUsageBuilder().WithFeatureId(Feature).WithOrderId("ord-other").WithExclusivity(exclusivity);

    // ── CONF-G1 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_CONF_G1_NullOrder_ThrowsArgumentNullException()
    {
        var act = () => TargetConflictEvaluator.Evaluate(null!, OrderTargetExclusivity.None, [Feature], []);

        act.Should().Throw<ArgumentNullException>().WithParameterName("order");
    }

    [Fact]
    public void Evaluate_CONF_G1_NullUsages_ThrowsArgumentNullException()
    {
        var act = () => TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.None, [Feature], null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("usages");
    }

    [Fact]
    public void Evaluate_CONF_G1_NullFeatureIds_ThrowsArgumentNullException()
    {
        var act = () => TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.None, null!, []);

        act.Should().Throw<ArgumentNullException>().WithParameterName("featureIds");
    }

    // ── CONF-B1 ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderTargetExclusivity.None)]
    [InlineData(OrderTargetExclusivity.WarnOnOverlap)]
    [InlineData(OrderTargetExclusivity.BlockOnOverlap)]
    public void Evaluate_CONF_B1_NoUsagesForFeature_IsNoneWhateverOwnExclusivityIs(OrderTargetExclusivity own)
    {
        var result = TargetConflictEvaluator.Evaluate(Order(), own, [Feature], []);

        result[Feature].Should().Be(OrderTargetConflictLevel.None);
    }

    [Fact]
    public void Evaluate_CONF_B1_UsageOfAnotherFeatureOnly_LeavesThisFeatureNone()
    {
        var usage = Usage(OrderTargetExclusivity.BlockOnOverlap).WithFeatureId("feat-other").Build();

        var result = TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.BlockOnOverlap, [Feature], [usage]);

        result.Should().ContainSingle().Which.Should().Be(KeyValuePair.Create(Feature, OrderTargetConflictLevel.None));
    }

    // ── CONF-B2 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_CONF_B2_UsageByTheOrderItself_IsIgnored()
    {
        var usage = Usage(OrderTargetExclusivity.BlockOnOverlap).WithOrderId(OrderId).Build();

        var result = TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.BlockOnOverlap, [Feature], [usage]);

        result[Feature].Should().Be(OrderTargetConflictLevel.None);
    }

    // ── CONF-B3 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_CONF_B3_UsageByTheOrdersParent_IsIgnored()
    {
        var usage = Usage(OrderTargetExclusivity.BlockOnOverlap).WithOrderId("ord-parent").Build();

        var result = TargetConflictEvaluator.Evaluate(Order(parentOrderId: "ord-parent"), OrderTargetExclusivity.BlockOnOverlap, [Feature], [usage]);

        result[Feature].Should().Be(OrderTargetConflictLevel.None);
    }

    [Fact]
    public void Evaluate_CONF_B3_UsageByTheOrdersChild_IsIgnored()
    {
        var usage = Usage(OrderTargetExclusivity.BlockOnOverlap).WithOrderId("ord-child").WithParentOrderId(OrderId).Build();

        var result = TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.BlockOnOverlap, [Feature], [usage]);

        result[Feature].Should().Be(OrderTargetConflictLevel.None);
    }

    [Fact]
    public void Evaluate_CONF_B3_UsageByASiblingWithSameNonNullParent_IsIgnored()
    {
        var usage = Usage(OrderTargetExclusivity.BlockOnOverlap).WithOrderId("ord-sibling").WithParentOrderId("ord-parent").Build();

        var result = TargetConflictEvaluator.Evaluate(Order(parentOrderId: "ord-parent"), OrderTargetExclusivity.BlockOnOverlap, [Feature], [usage]);

        result[Feature].Should().Be(OrderTargetConflictLevel.None);
    }

    [Fact]
    public void Evaluate_CONF_B3_TwoRootOrders_BothParentsNull_AreNotSiblings()
    {
        var usage = Usage(OrderTargetExclusivity.BlockOnOverlap).WithParentOrderId(null).Build();

        var result = TargetConflictEvaluator.Evaluate(Order(parentOrderId: null), OrderTargetExclusivity.None, [Feature], [usage]);

        result[Feature].Should().Be(OrderTargetConflictLevel.Blocked);
    }

    [Fact]
    public void Evaluate_CONF_B3_UsageByAChildOfAnotherParent_IsNotIgnored()
    {
        var usage = Usage(OrderTargetExclusivity.WarnOnOverlap).WithOrderId("ord-cousin").WithParentOrderId("ord-other-parent").Build();

        var result = TargetConflictEvaluator.Evaluate(Order(parentOrderId: "ord-parent"), OrderTargetExclusivity.None, [Feature], [usage]);

        result[Feature].Should().Be(OrderTargetConflictLevel.Warning);
    }

    [Fact]
    public void Evaluate_CONF_B3_FamilyIdsDifferingOnlyByCase_AreNotFamily()
    {
        var usage = Usage(OrderTargetExclusivity.BlockOnOverlap).WithOrderId("ORD-PARENT").Build();

        var result = TargetConflictEvaluator.Evaluate(Order(parentOrderId: "ord-parent"), OrderTargetExclusivity.None, [Feature], [usage]);

        result[Feature].Should().Be(OrderTargetConflictLevel.Blocked);
    }

    [Fact]
    public void Evaluate_CONF_B3_FamilyUsageIgnored_ButUnrelatedUsageOfSameFeatureStillCounts()
    {
        var family = Usage(OrderTargetExclusivity.BlockOnOverlap).WithOrderId("ord-child").WithParentOrderId(OrderId).Build();
        var stranger = Usage(OrderTargetExclusivity.WarnOnOverlap).WithOrderId("ord-stranger").Build();

        var result = TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.None, [Feature], [family, stranger]);

        result[Feature].Should().Be(OrderTargetConflictLevel.Warning);
    }

    // ── CONF-B4 ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderTargetExclusivity.None)]
    [InlineData(OrderTargetExclusivity.WarnOnOverlap)]
    [InlineData(OrderTargetExclusivity.BlockOnOverlap)]
    public void Evaluate_CONF_B4_OwnBlockOnOverlapWithAnyRemainingUsage_IsBlocked(OrderTargetExclusivity other)
    {
        var result = TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.BlockOnOverlap, [Feature], [Usage(other).Build()]);

        result[Feature].Should().Be(OrderTargetConflictLevel.Blocked);
    }

    [Theory]
    [InlineData(OrderTargetExclusivity.None)]
    [InlineData(OrderTargetExclusivity.WarnOnOverlap)]
    public void Evaluate_CONF_B4_OtherOrderBlockOnOverlap_IsBlockedWhateverOwnExclusivityIs(OrderTargetExclusivity own)
    {
        var result = TargetConflictEvaluator.Evaluate(Order(), own, [Feature], [Usage(OrderTargetExclusivity.BlockOnOverlap).Build()]);

        result[Feature].Should().Be(OrderTargetConflictLevel.Blocked);
    }

    [Fact]
    public void Evaluate_CONF_B4_OneBlockingUsageAmongWeakerOnes_IsBlockedRegardlessOfUsageOrder()
    {
        var weak = Usage(OrderTargetExclusivity.None).WithOrderId("ord-a").Build();
        var warn = Usage(OrderTargetExclusivity.WarnOnOverlap).WithOrderId("ord-b").Build();
        var block = Usage(OrderTargetExclusivity.BlockOnOverlap).WithOrderId("ord-c").Build();

        TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.None, [Feature], [block, warn, weak])[Feature]
            .Should().Be(OrderTargetConflictLevel.Blocked);
        TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.None, [Feature], [weak, warn, block])[Feature]
            .Should().Be(OrderTargetConflictLevel.Blocked);
    }

    // ── CONF-B5 ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OrderTargetExclusivity.None)]
    [InlineData(OrderTargetExclusivity.WarnOnOverlap)]
    public void Evaluate_CONF_B5_OwnWarnOnOverlapWithNonBlockingUsage_IsWarning(OrderTargetExclusivity other)
    {
        var result = TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.WarnOnOverlap, [Feature], [Usage(other).Build()]);

        result[Feature].Should().Be(OrderTargetConflictLevel.Warning);
    }

    [Fact]
    public void Evaluate_CONF_B5_OtherOrderWarnOnOverlapWithOwnNone_IsWarning()
    {
        var result = TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.None, [Feature], [Usage(OrderTargetExclusivity.WarnOnOverlap).Build()]);

        result[Feature].Should().Be(OrderTargetConflictLevel.Warning);
    }

    // ── CONF-B6 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_CONF_B6_OwnNoneAndEveryOtherNone_IsNone()
    {
        var first = Usage().WithOrderId("ord-a").Build();
        var second = Usage().WithOrderId("ord-b").Build();

        var result = TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.None, [Feature], [first, second]);

        result[Feature].Should().Be(OrderTargetConflictLevel.None);
    }

    // ── CONF-B7 ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void Evaluate_CONF_B7_UndefinedOwnExclusivity_ThrowsArgumentOutOfRangeException(int undefined)
    {
        var act = () => TargetConflictEvaluator.Evaluate(Order(), (OrderTargetExclusivity)undefined, [Feature], []);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Evaluate_CONF_B7_UndefinedUsageExclusivity_ThrowsArgumentOutOfRangeException()
    {
        var usage = Usage((OrderTargetExclusivity)42).Build();

        var act = () => TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.None, [Feature], [usage]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Evaluate_CONF_B7_UndefinedExclusivityOnAnIgnoredUsage_StillThrows()
    {
        var ownUsage = Usage((OrderTargetExclusivity)42).WithOrderId(OrderId).Build();

        var act = () => TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.None, [Feature], [ownUsage]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── CONF-B8 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_CONF_B8_ReturnsOneEntryPerInputFeatureIncludingNone()
    {
        var blocked = Usage(OrderTargetExclusivity.BlockOnOverlap).WithFeatureId("feat-b").Build();
        var warned = Usage(OrderTargetExclusivity.WarnOnOverlap).WithFeatureId("feat-w").Build();
        var unrequested = Usage(OrderTargetExclusivity.BlockOnOverlap).WithFeatureId("feat-unrequested").Build();

        var result = TargetConflictEvaluator.Evaluate(
            Order(), OrderTargetExclusivity.None, ["feat-n", "feat-w", "feat-b"], [blocked, warned, unrequested]);

        result.Should().BeEquivalentTo(new Dictionary<string, OrderTargetConflictLevel>(StringComparer.Ordinal)
        {
            ["feat-n"] = OrderTargetConflictLevel.None,
            ["feat-w"] = OrderTargetConflictLevel.Warning,
            ["feat-b"] = OrderTargetConflictLevel.Blocked,
        });
    }

    [Fact]
    public void Evaluate_CONF_B8_RepeatedInputFeatureId_YieldsASingleEntry()
    {
        var result = TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.None, [Feature, Feature], []);

        result.Should().ContainSingle().Which.Key.Should().Be(Feature);
    }

    [Fact]
    public void Evaluate_CONF_B8_NoInputFeatures_ReturnsEmptyResult()
    {
        var result = TargetConflictEvaluator.Evaluate(Order(), OrderTargetExclusivity.BlockOnOverlap, [], [Usage(OrderTargetExclusivity.BlockOnOverlap).Build()]);

        result.Should().BeEmpty();
    }
}
