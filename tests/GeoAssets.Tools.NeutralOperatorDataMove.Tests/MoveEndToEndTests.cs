using System.Text.Json;
using FluentAssertions;
using GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests;

/// <summary>
/// XD01-391 acceptance: a kernel database seeded with NeutralOperator orders in several states — created, approved with a pending
/// saga and its staged outbox batch, staged change sets, closed with released targets, a fan-out parent and child, an order of a
/// type with no target policy — plus rows that must stay (a kernel order, a remote vertical's ledger row, delivered history).
/// The tool plans, refuses the pending outbox, copies, verifies and removes; NeutralOperator.Server (its real host, over the
/// target database) serves each moved order with the content the kernel served, and the ledger reservations keep holding.
/// The gateway's <c>/api/v/neutral-operator/**</c> to <c>/api/**</c> rewrite in front of that host is covered by the gateway's own tests;
/// the orders are read here on the host's own <c>/api/workflow/service-orders</c> routes.
/// </summary>
public sealed class MoveEndToEndTests : IAsyncLifetime
{
    private MoveEndToEndRun run = null!;

    public async Task InitializeAsync() => run = await MoveEndToEndRun.SharedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void Plan_OverTheSeededKernel_ReportsEveryMovedTableAndExitsZero()
    {
        run.Plan.ExitCode.Should().Be(0, run.Plan.Text);
        var text = run.Plan.Text;
        text.Should().Contain("plan (dry run, nothing written)");
        text.Should().Contain("outbox.pending", "the staged approval batch is undelivered");
        Row(text, "public.ServiceOrders").Should().Equal(new long[] { 6, 0, 6, 0, 0, 0 }); // the six NeutralOperator orders, none in the target yet
        Row(text, "public.OrderActionLogs").Should().Equal(new long[] { 9, 0, 9, 0, 0, 0 }); // the kernel order's log does not move
        Row(text, "public.OrderDispatches").Should().Equal(new long[] { 3, 0, 3, 0, 0, 0 }); // the kernel order's dispatch does not move
        Row(text, "public.ServiceOrderTargets").Should().Equal(new long[] { 5, 0, 5, 0, 0, 0 }); // the kernel order's and the remote vertical's rows do not move
        Row(text, "public.WorkflowOutboxMessages").Should().Equal(new long[] { 2, 0, 2, 0, 0, 0 }); // only the two undelivered messages of moved orders
        Row(text, "neutral_operator.occupancy_apply_sagas").Should().Equal(new long[] { 1, 0, 1, 0, 0, 0 });
        Row(text, "neutral_operator.occupancy_apply_saga_poles").Should().Equal(new long[] { 2, 0, 2, 0, 0, 0 });
        Row(text, "neutral_operator.ServiceOrderOccupancyChangeSets").Should().Equal(new long[] { 4, 0, 4, 0, 0, 0 });
        run.KernelBeforeMove.Differences(run.KernelAfterCopy).Should().BeEmpty("plan and copy never write to the kernel");
    }

    [Fact]
    public void Copy_WithPendingOutboxButWithoutTheFlag_RefusesAndWritesNothing()
    {
        run.CopyWithoutFlag.ExitCode.Should().Be(3, run.CopyWithoutFlag.Text);
        run.CopyWithoutFlag.Text.Should().Contain("REFUSAL").And.Contain("outbox.pending").And.Contain("refused; nothing was changed");
        run.TargetAfterRefusedCopy.RowsOf("public.\"ServiceOrders\"").Should().Be(0);
        run.TargetAfterRefusedCopy.Digests.Values.Should().OnlyContain(v => v.StartsWith("0:", StringComparison.Ordinal), "a refused copy leaves the target empty");
    }

    [Fact]
    public void Copy_WithIncludePendingOutbox_CopiesEverythingAndVerifyAgrees()
    {
        run.Copy.ExitCode.Should().Be(0, run.Copy.Text);
        Row(run.Copy.Text, "public.ServiceOrders")[4].Should().Be(6, "Copied");
        run.Verify.ExitCode.Should().Be(0, run.Verify.Text);
        run.Verify.Text.Should().Contain("every moved row is in the target, identical");
        run.TargetAfterCopy.RowsOf("public.\"ServiceOrders\"").Should().Be(6);
        run.TargetAfterCopy.RowsOf("public.\"OrderActionLogs\"").Should().Be(9);
        run.TargetAfterCopy.RowsOf("public.\"WorkflowOutboxMessages\"").Should().Be(2);
        run.TargetAfterCopy.RowsOf("neutral_operator.occupancy_apply_sagas").Should().Be(1, "the pending saga follows its order");
        run.TargetAfterCopy.RowsOf("neutral_operator.\"ServiceOrderOccupancyChangeSets\"").Should().Be(4, "staged and discarded change sets follow their orders");
    }

    [Fact]
    public async Task Copy_DoesNotBringAnythingThatIsNotNeutralOperatorsAcross()
    {
        (await Sql.CountAsync(run.Target, "public.\"ServiceOrders\"", "\"Id\" = 'kernel-inspection'")).Should().Be(0);
        (await Sql.CountAsync(run.Target, "public.\"ServiceOrderTargets\"", "\"Id\" IN ('t-kernel-z','t-remote-fs')")).Should().Be(0);
        (await Sql.CountAsync(run.Target, "public.\"OrderActionLogs\"", "\"ServiceOrderId\" = 'kernel-inspection'")).Should().Be(0);
        (await Sql.CountAsync(run.Target, "public.\"WorkflowOutboxMessages\"", "\"PartitionKey\" = 'kernel-inspection' OR \"Status\" = 2")).Should().Be(0, "delivered history and other orders' messages stay in the kernel");
    }

    [Fact]
    public void NeutralOperatorServer_AfterTheCopy_ServesEveryMovedOrderWithTheContentTheKernelServed()
    {
        run.ServedByKernel.Select(s => s.OrderId).Should().Equal(KernelSeed.Moved);
        run.ServedByTargetAfterCopy.Should().HaveSameCount(run.ServedByKernel);

        foreach (var (before, after) in run.ServedByKernel.Zip(run.ServedByTargetAfterCopy))
        {
            foreach (var route in ServedOrder.Routes)
                after.Bodies[route].Should().Be(before.Bodies[route], $"{before.OrderId}{route} must read the same on the new host");
        }
    }

    [Fact]
    public void NeutralOperatorServer_ServesTheStatesTheSeedPutInEachOrder()
    {
        var byId = run.ServedByTargetAfterCopy.ToDictionary(s => s.OrderId, StringComparer.Ordinal);

        byId[KernelSeed.Created].Order.GetProperty("status").GetString().Should().Be("Draft");
        var approved = byId[KernelSeed.Approved].Order;
        approved.GetProperty("attributes").GetProperty("occupancy.apply.state").GetString().Should().Be("awaiting-apply");
        approved.GetProperty("actionLog").GetArrayLength().Should().Be(3, "the log keeps its order");
        JsonDocument.Parse(byId[KernelSeed.Approved].Bodies["/targets"]).RootElement.GetProperty("totalCount").GetInt32().Should().Be(2);
        JsonDocument.Parse(byId[KernelSeed.Approved].Bodies["/occupancy-changes"]).RootElement.GetProperty("totalCount").GetInt32().Should().Be(2, "the staged change sets");
        byId[KernelSeed.Closed].Order.GetProperty("status").GetString().Should().Be("Cancelled");
        byId[KernelSeed.Parent].Order.GetProperty("childOrderIds").EnumerateArray().Select(e => e.GetString()).Should().Equal(KernelSeed.Child);
        byId[KernelSeed.Parent].Order.GetProperty("dispatches").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Ledger_AfterTheCopy_TheKernelsKernelVerticalRowsEqualTheTargetsLocalTargets()
    {
        var kernel = run.LedgerRowsBeforeRemove.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => r.Value).ToList();
        var target = await Sql.ColumnAsync(run.Target, "SELECT to_jsonb(t)::text FROM public.\"ServiceOrderTargets\" t ORDER BY \"Id\"");

        kernel.Should().HaveCount(5);
        target.Should().Equal(kernel, "the reservations the kernel's ledger holds are the very rows the target now serves");
    }

    [Fact]
    public void Remove_AfterAVerifiedCopy_ExitsZeroAndRemovesTheMovedRows()
    {
        run.Remove.ExitCode.Should().Be(0, run.Remove.Text);
        run.Remove.Text.Should().Contain("removed from the kernel's database");
        run.KernelAfterRemove.RowsOf("public.\"ServiceOrders\"").Should().Be(1, "only the kernel's own order is left");
        run.KernelAfterRemove.RowsOf("public.\"OrderActionLogs\"").Should().Be(1);
        run.KernelAfterRemove.RowsOf("public.\"OrderDispatches\"").Should().Be(1);
        run.KernelAfterRemove.RowsOf("public.\"WorkflowOutboxMessages\"").Should().Be(2, "the delivered history and the other order's message stay");
        run.KernelAfterRemove.RowsOf("neutral_operator.occupancy_apply_sagas").Should().Be(0);
        run.KernelAfterRemove.RowsOf("neutral_operator.\"ServiceOrderOccupancyChangeSets\"").Should().Be(0);
        run.KernelAfterRemove.RowsOf("neutral_operator.field_pole_occupancies").Should().Be(0);
        run.TargetAfterRemove.Should().Be(run.TargetAfterCopy, "remove never writes to the target");
    }

    [Fact]
    public async Task Remove_LeavesTheKernelOrderTheRemoteLedgerRowAndTheDeliveredHistoryUntouched()
    {
        (await Sql.CountAsync(run.Kernel, "public.\"ServiceOrders\"", "\"Id\" = 'kernel-inspection'")).Should().Be(1);
        (await Sql.CountAsync(run.Kernel, "public.\"ServiceOrderTargets\"", "\"Id\" IN ('t-kernel-z','t-remote-fs')")).Should().Be(2);
        (await Sql.ScalarAsync(run.Kernel, """SELECT "VerticalId" FROM public."ServiceOrderTargets" WHERE "Id" = 't-remote-fs'""")).Should().Be("field-service");
        (await Sql.CountAsync(run.Kernel, "public.\"WorkflowOutboxMessages\"", "\"Status\" = 2 AND \"PartitionKey\" = 'no-approved'")).Should().Be(1, "dispatched history is not moved and not deleted");
        (await Sql.CountAsync(run.Kernel, "public.\"WorkflowOutboxMessages\"", "\"PartitionKey\" = 'kernel-inspection'")).Should().Be(1);
    }

    [Fact]
    public void Remove_ReKeysTheMovedOrdersLedgerRowsToTheVertical_KeepingEveryIdAndValue()
    {
        run.LedgerRowsBeforeRemove.Keys.Should().BeEquivalentTo("t-approved-a", "t-approved-b", "t-staged-c", "t-closed-d", "t-parent-e");
        run.LedgerRowsAfterRemove.Keys.Should().BeEquivalentTo(run.LedgerRowsBeforeRemove.Keys, "the ledger keeps every reservation, released ones included");

        foreach (var (id, before) in run.LedgerRowsBeforeRemove)
        {
            var kernelRow = JsonDocument.Parse(before).RootElement;
            var ledgerRow = JsonDocument.Parse(run.LedgerRowsAfterRemove[id]).RootElement;

            ledgerRow.GetProperty("VerticalId").GetString().Should().Be("neutral-operator", id);
            ledgerRow.GetProperty("KernelOrderId").ValueKind.Should().Be(JsonValueKind.Null, id);
            foreach (var column in new[] { "ServiceOrderId", "FeatureId", "Status", "Version", "ReleasedAt", "OrganizationId", "AddedAt", "AddedBy", "Longitude", "Latitude" })
                ledgerRow.GetProperty(column).ToString().Should().Be(kernelRow.GetProperty(column).ToString(), $"{id}.{column}");
        }

        run.LedgerRowsAfterRemove["t-closed-d"].Should().Contain("\"ReleasedAt\"").And.NotContain("\"ReleasedAt\": null", "a released reservation stays released");
    }

    [Fact]
    public void Remove_KeepsTheExclusivityThatHoldsAFeature_ForTheNextReservation()
    {
        var before = run.UsagesBeforeRemove.Where(u => !string.Equals(u.Usage.OrderId, KernelSeed.KernelOrder, StringComparison.Ordinal)).ToList();
        var after = run.UsagesAfterRemove.Where(u => !string.Equals(u.Usage.OrderId, KernelSeed.KernelOrder, StringComparison.Ordinal) && !string.Equals(u.Usage.VerticalId, "field-service", StringComparison.Ordinal)).ToList();

        before.Select(u => u.Usage.FeatureId).Should().BeEquivalentTo("pole-a", "pole-b", "pole-c", "pole-e");  // the released pole-d holds nothing
        before.Should().OnlyContain(u => string.Equals(u.Usage.VerticalId, TargetLedger.KernelVerticalId, StringComparison.Ordinal));
        after.Select(u => u.Usage.FeatureId).Should().BeEquivalentTo(before.Select(u => u.Usage.FeatureId));
        after.Should().OnlyContain(u => string.Equals(u.Usage.VerticalId, "neutral-operator", StringComparison.Ordinal));

        foreach (var held in before)
        {
            var now = after.Single(u => string.Equals(u.Usage.FeatureId, held.Usage.FeatureId, StringComparison.Ordinal));
            now.Usage.OrderId.Should().Be(held.Usage.OrderId);
            now.Usage.OrderTypeId.Should().Be(held.Usage.OrderTypeId);
            now.Usage.Exclusivity.Should().Be(held.Usage.Exclusivity, $"{held.Usage.FeatureId} is held exactly as strongly as before");
            now.OrganizationId.Should().Be(held.OrganizationId);
        }

        before.Single(u => string.Equals(u.Usage.FeatureId, "pole-a", StringComparison.Ordinal)).Usage.Exclusivity.Should().Be(OrderTargetExclusivity.WarnOnOverlap, "the pole-work policy is what the kernel enforced");
        before.Single(u => string.Equals(u.Usage.FeatureId, "pole-c", StringComparison.Ordinal)).Usage.Exclusivity.Should().Be(OrderTargetExclusivity.None, "a type without a target policy holds with none");
    }

    [Fact]
    public void NeutralOperatorServer_AfterTheRemove_StillServesTheSameOrders()
    {
        foreach (var (before, after) in run.ServedByKernel.Zip(run.ServedByTargetAfterRemove))
        {
            foreach (var route in ServedOrder.Routes)
                after.Bodies[route].Should().Be(before.Bodies[route], $"{before.OrderId}{route}");
        }
    }

    private static long[] Row(string report, string table) => ReportRows.Of(report, table);
}
