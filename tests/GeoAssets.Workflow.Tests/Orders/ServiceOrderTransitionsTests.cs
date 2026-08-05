using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

public class ServiceOrderTransitionsTests
{
    // ── Legal edges ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ServiceOrderStatus.Draft,      ServiceOrderStatus.Pending)]
    [InlineData(ServiceOrderStatus.Draft,      ServiceOrderStatus.Cancelled)]
    [InlineData(ServiceOrderStatus.Pending,    ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.Pending,    ServiceOrderStatus.Cancelled)]
    [InlineData(ServiceOrderStatus.InProgress, ServiceOrderStatus.OnHold)]
    [InlineData(ServiceOrderStatus.InProgress, ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.InProgress, ServiceOrderStatus.Cancelled)]
    [InlineData(ServiceOrderStatus.OnHold,     ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.OnHold,     ServiceOrderStatus.Cancelled)]
    public void IsValid_LegalTransition_ReturnsTrue(string from, string to)
    {
        ServiceOrderTransitions.IsValid(from, to).Should().BeTrue();
    }

    // ── Same-status no-op ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.Pending)]
    [InlineData(ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.OnHold)]
    [InlineData(ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.Cancelled)]
    public void IsValid_SameStatus_ReturnsTrue(string status)
    {
        ServiceOrderTransitions.IsValid(status, status).Should().BeTrue();
    }

    // ── Illegal edges ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ServiceOrderStatus.Draft,      ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.Draft,      ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.Draft,      ServiceOrderStatus.OnHold)]
    [InlineData(ServiceOrderStatus.Pending,    ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.Pending,    ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.Pending,    ServiceOrderStatus.OnHold)]
    [InlineData(ServiceOrderStatus.InProgress, ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.InProgress, ServiceOrderStatus.Pending)]
    [InlineData(ServiceOrderStatus.OnHold,     ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.OnHold,     ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.Completed,  ServiceOrderStatus.InProgress)]
    [InlineData(ServiceOrderStatus.Completed,  ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.Cancelled,  ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.Cancelled,  ServiceOrderStatus.InProgress)]
    public void IsValid_IllegalTransition_ReturnsFalse(string from, string to)
    {
        ServiceOrderTransitions.IsValid(from, to).Should().BeFalse();
    }

    // ── OrderType-aware IsValid ─────────────────────────────────────────────────

    [Fact]
    public void IsValid_WithOrderType_NullOrderType_FallsBackToGlobalGraph()
    {
        ServiceOrderTransitions.IsValid((OrderType?)null, ServiceOrderStatus.Draft, ServiceOrderStatus.Pending)
            .Should().BeTrue();
        ServiceOrderTransitions.IsValid((OrderType?)null, ServiceOrderStatus.Draft, ServiceOrderStatus.Completed)
            .Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithOrderType_NoStatesDefined_FallsBackToGlobalGraph()
    {
        var orderType = new OrderType { Id = "t", DisplayName = "T" };

        ServiceOrderTransitions.IsValid(orderType, ServiceOrderStatus.Draft, ServiceOrderStatus.Pending)
            .Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithOrderType_CustomGraph_AllowsEdgeAbsentFromGlobalGraph()
    {
        // "Draft -> Completed" is illegal in the global graph, but this custom
        // order type's own graph explicitly allows it — proving the custom graph
        // is consulted instead of the global one, not merely in addition to it.
        var orderType = new OrderType
        {
            Id     = "t",
            DisplayName = "T",
            States = [new(ServiceOrderStatus.Draft, "Draft"), new(ServiceOrderStatus.Completed, "Completed", IsSuccess: true)],
            Transitions = [new(ServiceOrderStatus.Draft, ServiceOrderStatus.Completed)],
        };

        ServiceOrderTransitions.IsValid(orderType, ServiceOrderStatus.Draft, ServiceOrderStatus.Completed)
            .Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithOrderType_CustomGraph_RejectsEdgeThatOnlyGlobalGraphAllows()
    {
        // "Draft -> Pending" is legal globally, but this custom order type's graph
        // never defines it — proving the custom graph fully replaces the global one
        // rather than only adding to it.
        var orderType = new OrderType
        {
            Id     = "t",
            DisplayName = "T",
            States = [new(ServiceOrderStatus.Draft, "Draft"), new(ServiceOrderStatus.Cancelled, "Cancelled")],
            Transitions = [new(ServiceOrderStatus.Draft, ServiceOrderStatus.Cancelled)],
        };

        ServiceOrderTransitions.IsValid(orderType, ServiceOrderStatus.Draft, ServiceOrderStatus.Pending)
            .Should().BeFalse();
    }

    [Fact]
    public void IsValid_WithOrderType_CustomGraph_SameStatusIsAlwaysNoOp()
    {
        var orderType = new OrderType
        {
            Id     = "t",
            DisplayName = "T",
            States = [new("UnderReview", "Under Review")],
            Transitions = [],
        };

        ServiceOrderTransitions.IsValid(orderType, "UnderReview", "UnderReview").Should().BeTrue();
    }

    // ── HasTransitionFor ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ServiceOrderStatus.Draft)]
    [InlineData(ServiceOrderStatus.Pending)]
    public void HasTransitionFor_NullOrderType_CancelFromDraftOrPending_ReturnsTrue(string from)
    {
        ServiceOrderTransitions.HasTransitionFor(null, from, OrderActionType.Cancel).Should().BeTrue();
    }

    [Fact]
    public void HasTransitionFor_NullOrderType_CancelFromInProgress_ReturnsFalse()
    {
        ServiceOrderTransitions.HasTransitionFor(null, ServiceOrderStatus.InProgress, OrderActionType.Cancel)
            .Should().BeFalse();
    }

    [Fact]
    public void HasTransitionFor_NullOrderType_NonCancelAction_ReturnsFalse()
    {
        ServiceOrderTransitions.HasTransitionFor(null, ServiceOrderStatus.Draft, OrderActionType.Approve)
            .Should().BeFalse();
    }

    [Fact]
    public void HasTransitionFor_CustomOrderType_MatchingTriggerAction_ReturnsTrue()
    {
        var orderType = new OrderType
        {
            Id     = "t",
            DisplayName = "T",
            States = [new("UnderReview", "Under Review"), new("Cancelled", "Cancelled")],
            Transitions = [new("UnderReview", "Cancelled", OrderActionType.Cancel)],
        };

        ServiceOrderTransitions.HasTransitionFor(orderType, "UnderReview", OrderActionType.Cancel).Should().BeTrue();
    }

    [Fact]
    public void HasTransitionFor_CustomOrderType_NoMatchingEdge_ReturnsFalse()
    {
        // Custom order type defines a graph, but not one that allows Cancel from
        // Draft — must not fall back to the global "Draft is cancelable" default.
        var orderType = new OrderType
        {
            Id     = "t",
            DisplayName = "T",
            States = [new(ServiceOrderStatus.Draft, "Draft"), new("Approved", "Approved")],
            Transitions = [new(ServiceOrderStatus.Draft, "Approved", OrderActionType.Approve)],
        };

        ServiceOrderTransitions.HasTransitionFor(orderType, ServiceOrderStatus.Draft, OrderActionType.Cancel)
            .Should().BeFalse();
    }

    // ── IsSuccessState ───────────────────────────────────────────────────────────

    [Fact]
    public void IsSuccessState_NullOrderType_CompletedIsSuccess()
    {
        ServiceOrderTransitions.IsSuccessState(null, ServiceOrderStatus.Completed).Should().BeTrue();
    }

    [Fact]
    public void IsSuccessState_NullOrderType_OtherStatusIsNotSuccess()
    {
        ServiceOrderTransitions.IsSuccessState(null, ServiceOrderStatus.Cancelled).Should().BeFalse();
    }

    [Fact]
    public void IsSuccessState_CustomOrderType_UsesIsSuccessFlagRegardlessOfKeyName()
    {
        // The success state is named "Resolved", not "Completed" — proving success
        // is derived from the IsSuccess flag, not a literal "Completed" comparison.
        var orderType = new OrderType
        {
            Id     = "t",
            DisplayName = "T",
            States = [new("Resolved", "Resolved", IsSuccess: true), new(ServiceOrderStatus.Cancelled, "Cancelled")],
        };

        ServiceOrderTransitions.IsSuccessState(orderType, "Resolved").Should().BeTrue();
        ServiceOrderTransitions.IsSuccessState(orderType, ServiceOrderStatus.Cancelled).Should().BeFalse();
    }
}
