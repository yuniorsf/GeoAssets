using FluentAssertions;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

/// <summary>
/// XD01-220's AC: every edge in the 12-state transition matrix compiles as an explicit graph,
/// invalid transitions throw a typed domain exception, and both <see cref="FieldWorkStatus.Completed"/>
/// branches (<see cref="FieldWorkStatus.Inspected"/> vs <see cref="FieldWorkStatus.ConflictReview"/>)
/// are covered.
/// </summary>
public class FieldWorkWorkflowGraphTests
{
    private static readonly OrderType FieldWorkOrderType =
        FieldWorkWorkflowGraph.BuildOrderType("field-work", "Field Work Order");

    public static readonly TheoryData<string, string> ValidEdges = new()
    {
        { FieldWorkStatus.Draft, FieldWorkStatus.Pending },
        { FieldWorkStatus.Draft, FieldWorkStatus.Cancelled },
        { FieldWorkStatus.Pending, FieldWorkStatus.Scheduled },
        { FieldWorkStatus.Pending, FieldWorkStatus.Cancelled },
        { FieldWorkStatus.Scheduled, FieldWorkStatus.Dispatched },
        { FieldWorkStatus.Scheduled, FieldWorkStatus.Cancelled },
        { FieldWorkStatus.Dispatched, FieldWorkStatus.EnRoute },
        { FieldWorkStatus.Dispatched, FieldWorkStatus.Cancelled },
        { FieldWorkStatus.EnRoute, FieldWorkStatus.OnSite },
        { FieldWorkStatus.EnRoute, FieldWorkStatus.Cancelled },
        { FieldWorkStatus.OnSite, FieldWorkStatus.InProgress },
        { FieldWorkStatus.InProgress, FieldWorkStatus.Suspended },
        { FieldWorkStatus.Suspended, FieldWorkStatus.InProgress },
        { FieldWorkStatus.InProgress, FieldWorkStatus.Completed },
        { FieldWorkStatus.Completed, FieldWorkStatus.Inspected },
        { FieldWorkStatus.Completed, FieldWorkStatus.ConflictReview },
        { FieldWorkStatus.ConflictReview, FieldWorkStatus.Inspected },
        { FieldWorkStatus.ConflictReview, FieldWorkStatus.Pending },
    };

    [Theory]
    [MemberData(nameof(ValidEdges))]
    public void IsValid_EveryDocumentedEdge_ReturnsTrue(string from, string to) =>
        ServiceOrderTransitions.IsValid(FieldWorkOrderType, from, to).Should().BeTrue($"'{from}' -> '{to}' is a documented edge of FieldWorkWorkflowGraph");

    public static readonly TheoryData<string, string> IllegalEdges = new()
    {
        { FieldWorkStatus.Draft, FieldWorkStatus.Completed },
        { FieldWorkStatus.Draft, FieldWorkStatus.Scheduled },
        { FieldWorkStatus.OnSite, FieldWorkStatus.Cancelled },
        { FieldWorkStatus.InProgress, FieldWorkStatus.Cancelled },
        { FieldWorkStatus.Completed, FieldWorkStatus.Cancelled },
        { FieldWorkStatus.Inspected, FieldWorkStatus.InProgress },
        { FieldWorkStatus.ConflictReview, FieldWorkStatus.Completed },
        { FieldWorkStatus.Cancelled, FieldWorkStatus.Draft },
        { FieldWorkStatus.EnRoute, FieldWorkStatus.Dispatched },
    };

    [Theory]
    [MemberData(nameof(IllegalEdges))]
    public void IsValid_UndocumentedEdge_ReturnsFalse(string from, string to) =>
        ServiceOrderTransitions.IsValid(FieldWorkOrderType, from, to).Should().BeFalse($"'{from}' -> '{to}' is not a documented edge of FieldWorkWorkflowGraph");

    [Fact]
    public void Transition_IllegalEdge_ThrowsInvalidServiceOrderTransitionException()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.OnSite };

        var act = () => order.Transition(FieldWorkStatus.Cancelled, TimeProvider.System, FieldWorkOrderType);

        act.Should().Throw<InvalidServiceOrderTransitionException>()
            .Which.Should().Match<InvalidServiceOrderTransitionException>(
                e => e.From == FieldWorkStatus.OnSite && e.To == FieldWorkStatus.Cancelled);
    }

    [Fact]
    public void Transition_ToInspected_IsSuccessStateAndStampsCompletedAt()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.Completed };

        order.Transition(FieldWorkStatus.Inspected, TimeProvider.System, FieldWorkOrderType);

        order.Status.Should().Be(FieldWorkStatus.Inspected);
        order.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Transition_ToConflictReview_IsNotSuccessStateAndDoesNotStampCompletedAt()
    {
        var order = new ServiceOrder { Status = FieldWorkStatus.Completed };

        order.Transition(FieldWorkStatus.ConflictReview, TimeProvider.System, FieldWorkOrderType);

        order.Status.Should().Be(FieldWorkStatus.ConflictReview);
        order.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Transition_ToCompleted_IsNotItselfASuccessState()
    {
        // Completed is an intermediate "work physically finished, pending review" state in this
        // graph, not the terminal success state — Inspected is. Confirms the two Completed
        // branches really are distinguishable, not an accidental naming collision with the
        // default graph's ServiceOrderStatus.Completed semantics.
        var order = new ServiceOrder { Status = FieldWorkStatus.InProgress };

        order.Transition(FieldWorkStatus.Completed, TimeProvider.System, FieldWorkOrderType);

        order.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void BuildOrderType_InitialStateKey_IsDraft() =>
        FieldWorkOrderType.InitialStateKey.Should().Be(FieldWorkStatus.Draft);

    [Fact]
    public void BuildOrderType_HasExactlyTwelveStates() =>
        FieldWorkOrderType.States.Should().HaveCount(12);
}
