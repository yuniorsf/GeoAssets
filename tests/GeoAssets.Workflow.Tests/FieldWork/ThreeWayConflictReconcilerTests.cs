using FluentAssertions;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

public class ThreeWayConflictReconcilerTests
{
    private static readonly OrderType FieldWorkOrderType =
        FieldWorkWorkflowGraph.BuildOrderType("field-work", "Field Work Order");

    private static ServiceOrder Order(string status, string? assignedTo = "crew-1", Dictionary<string, string>? attributes = null, List<OrderActionLog>? actionLog = null) => new()
    {
        Status = status,
        AssignedTo = assignedTo,
        Attributes = attributes ?? [],
        ActionLog = actionLog ?? [],
    };

    // ── Classify: TrivialCatchUp ──────────────────────────────────────────────

    [Fact]
    public void Classify_LocalIdenticalToBase_IsTrivialCatchUp()
    {
        var baseOrder = Order(FieldWorkStatus.InProgress, attributes: new(StringComparer.Ordinal) { ["note"] = "x" });
        var local = Order(FieldWorkStatus.InProgress, attributes: new(StringComparer.Ordinal) { ["note"] = "x" });
        var remote = Order(FieldWorkStatus.Completed);

        ThreeWayConflictReconciler.Classify(baseOrder, local, remote).Should().Be(ConflictClassification.TrivialCatchUp);
    }

    [Fact]
    public void Reconcile_TrivialCatchUp_AdoptsRemoteAsIs()
    {
        var baseOrder = Order(FieldWorkStatus.InProgress);
        var local = Order(FieldWorkStatus.InProgress);
        var remote = Order(FieldWorkStatus.Completed);

        var result = ThreeWayConflictReconciler.Reconcile(baseOrder, local, remote, TimeProvider.System);

        result.Status.Should().Be(FieldWorkStatus.Completed);
    }

    // ── Classify: StructuralCollision (the named "Complete racing Cancel/reassign" scenario) ──

    [Fact]
    public void Classify_LocalCompletedWhileRemoteCancelled_IsStructuralCollision()
    {
        var baseOrder = Order(FieldWorkStatus.InProgress);
        var local = Order(FieldWorkStatus.Completed);
        var remote = Order(FieldWorkStatus.Cancelled);

        ThreeWayConflictReconciler.Classify(baseOrder, local, remote).Should().Be(ConflictClassification.StructuralCollision);
    }

    [Fact]
    public void Classify_LocalCompletedWhileRemoteReassignedCrewWithNoStatusChange_IsStructuralCollision()
    {
        var baseOrder = Order(FieldWorkStatus.InProgress, assignedTo: "crew-1");
        var local = Order(FieldWorkStatus.Completed, assignedTo: "crew-1");
        var remote = Order(FieldWorkStatus.InProgress, assignedTo: "crew-2"); // dispatcher reassigned, status untouched

        ThreeWayConflictReconciler.Classify(baseOrder, local, remote).Should().Be(ConflictClassification.StructuralCollision);
    }

    [Fact]
    public void Classify_BothSidesIndependentlyReachCompleted_IsNotAStructuralCollision()
    {
        // Convergent outcome — not a collision even though both sides changed status.
        var baseOrder = Order(FieldWorkStatus.InProgress);
        var local = Order(FieldWorkStatus.Completed);
        var remote = Order(FieldWorkStatus.Completed);

        ThreeWayConflictReconciler.Classify(baseOrder, local, remote).Should().Be(ConflictClassification.BenignDivergence);
    }

    [Fact]
    public void Reconcile_StructuralCollision_EscalatesToConflictReview_EvenWhenRemoteStatusHasNoOutgoingGraphEdges()
    {
        // Cancelled has zero outgoing edges in FieldWorkWorkflowGraph — proves the escalation
        // really does bypass the normal graph-edge check, not merely avoid hitting it by luck.
        var baseOrder = Order(FieldWorkStatus.InProgress);
        var local = Order(FieldWorkStatus.Completed);
        var remote = Order(FieldWorkStatus.Cancelled);

        var result = ThreeWayConflictReconciler.Reconcile(baseOrder, local, remote, TimeProvider.System);

        result.Status.Should().Be(FieldWorkStatus.ConflictReview);
    }

    [Fact]
    public void Reconcile_StructuralCollision_NeverAutoCommitsLocalOrRemoteStatus()
    {
        var baseOrder = Order(FieldWorkStatus.InProgress);
        var local = Order(FieldWorkStatus.Completed);
        var remote = Order(FieldWorkStatus.Cancelled);

        var result = ThreeWayConflictReconciler.Reconcile(baseOrder, local, remote, TimeProvider.System);

        result.Status.Should().NotBe(FieldWorkStatus.Completed);
        result.Status.Should().NotBe(FieldWorkStatus.Cancelled);
    }

    // ── Classify/Reconcile: BenignDivergence ──────────────────────────────────

    [Fact]
    public void Reconcile_BenignNoteDivergence_MergesBothActionLogsAndPreservesOffilneOnlyAttribute()
    {
        var baseOrder = Order(FieldWorkStatus.InProgress, attributes: new(StringComparer.Ordinal) { ["shared"] = "base-value" });
        var localEntry = new OrderActionLog(OrderActionType.Annotate, "crew-1", new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc), "local note");
        var local = Order(FieldWorkStatus.InProgress,
            attributes: new(StringComparer.Ordinal) { ["shared"] = "base-value", ["offlineOnly"] = "set-while-offline" },
            actionLog: [localEntry]);

        var remoteEntry = new OrderActionLog(OrderActionType.Annotate, "dispatcher", new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc), "remote note");
        var remote = Order(FieldWorkStatus.Suspended,
            attributes: new(StringComparer.Ordinal) { ["shared"] = "base-value" },
            actionLog: [remoteEntry]);

        var result = ThreeWayConflictReconciler.Reconcile(baseOrder, local, remote, TimeProvider.System);

        result.Status.Should().Be(FieldWorkStatus.Suspended, "remote's status wins when there's no collision");
        result.Attributes.Should().Contain("offlineOnly", "set-while-offline");
        result.ActionLog.Should().BeEquivalentTo([localEntry, remoteEntry]);
    }

    [Fact]
    public void Reconcile_BenignDivergence_RemoteWinsOnASameKeyDoubleEdit()
    {
        var baseOrder = Order(FieldWorkStatus.InProgress, attributes: new(StringComparer.Ordinal) { ["note"] = "original" });
        var local = Order(FieldWorkStatus.InProgress, attributes: new(StringComparer.Ordinal) { ["note"] = "local edit" });
        var remote = Order(FieldWorkStatus.InProgress, attributes: new(StringComparer.Ordinal) { ["note"] = "remote edit" });

        var result = ThreeWayConflictReconciler.Reconcile(baseOrder, local, remote, TimeProvider.System);

        result.Attributes["note"].Should().Be("remote edit");
    }

    [Fact]
    public void Reconcile_BenignDivergence_DuplicateActionLogEntryIsNotDoubleCounted()
    {
        // A shared common action (e.g. the crew's own earlier Dispatch entry) present on both
        // sides must appear once in the merged log, not twice.
        var baseOrder = Order(FieldWorkStatus.InProgress);
        var sharedEntry = new OrderActionLog(OrderActionType.Dispatch, "dispatcher", new DateTime(2026, 1, 1, 7, 0, 0, DateTimeKind.Utc));
        var local = Order(FieldWorkStatus.InProgress, actionLog: [sharedEntry]);
        var remote = Order(FieldWorkStatus.Suspended, actionLog: [sharedEntry]);

        var result = ThreeWayConflictReconciler.Reconcile(baseOrder, local, remote, TimeProvider.System);

        result.ActionLog.Should().ContainSingle();
    }

    // ── Adjudicate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Adjudicate_Approved_RoutesToInspected()
    {
        var order = Order(FieldWorkStatus.ConflictReview);

        ThreeWayConflictReconciler.Adjudicate(order, approve: true, TimeProvider.System, FieldWorkOrderType);

        order.Status.Should().Be(FieldWorkStatus.Inspected);
    }

    [Fact]
    public void Adjudicate_Rejected_RoutesBackToPending()
    {
        var order = Order(FieldWorkStatus.ConflictReview);

        ThreeWayConflictReconciler.Adjudicate(order, approve: false, TimeProvider.System, FieldWorkOrderType);

        order.Status.Should().Be(FieldWorkStatus.Pending);
    }

    [Fact]
    public void Adjudicate_OrderNotInConflictReview_ThrowsInvalidServiceOrderTransitionException()
    {
        var order = Order(FieldWorkStatus.Draft);

        var act = () => ThreeWayConflictReconciler.Adjudicate(order, approve: true, TimeProvider.System, FieldWorkOrderType);

        act.Should().Throw<InvalidServiceOrderTransitionException>();
    }
}
