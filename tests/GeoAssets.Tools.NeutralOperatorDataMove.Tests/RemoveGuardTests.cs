using FluentAssertions;
using GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests;

/// <summary>
/// XD01-391 AC: <c>remove</c> deletes from the kernel only what the target provably holds, identically, and refuses — changing nothing —
/// the moment a row was altered, lost or added after the copy.
/// </summary>
public sealed class RemoveGuardTests
{
    private const string ServiceOrder = """
        INSERT INTO "ServiceOrders" ("Id","Title","Description","OrderTypeId","Status","Priority","CreatedBy","OrganizationId","CreatedAt","AttributesJson","FeatureIdsJson","RowVersion")
        VALUES ('no-late','Created after the copy','d','shared-use-pole-work','Draft',1,'owner-admin','a0a0a0a0-0000-4000-8000-000000000382','2026-11-20T10:00:00Z','{}','[]','\x00')
        """;

    public static TheoryData<string, string, string> AlterationsAfterTheCopy => new()
    {
        { "kernel", "a kernel order's title changed", """UPDATE public."ServiceOrders" SET "Title" = 'altered' WHERE "Id" = 'no-created'""" },
        { "kernel", "an action log entry was appended to a moved order", """INSERT INTO public."OrderActionLogs" ("ServiceOrderId","ActionCode","PerformedBy","PerformedAt","Comment","ActorKind") VALUES ('no-approved','Comment','owner-admin','2026-11-20T10:00:00Z','late',0)""" },
        { "kernel", "a dispatch was removed from a moved order", """DELETE FROM public."OrderDispatches" WHERE "ServiceOrderId" = 'no-parent' AND "DispatchedBy" = 'owner-admin' AND "Note" = 'first occupant'""" },
        { "kernel", "a change set was committed", """UPDATE neutral_operator."ServiceOrderOccupancyChangeSets" SET "Status" = 'Committed', "ClosedAt" = '2026-11-20T10:00:00Z' WHERE "Id" = 'cccccccc-0000-4000-8000-000000000001'""" },
        { "kernel", "the saga moved on", """UPDATE neutral_operator.occupancy_apply_sagas SET "State" = 'Committed', "CompletedAt" = '2026-11-20T10:00:00Z' WHERE "ServiceOrderId" = 'no-approved'""" },
        { "kernel", "a ledger row's version moved", """UPDATE public."ServiceOrderTargets" SET "Version" = "Version" + 1 WHERE "Id" = 't-approved-a'""" },
        { "kernel", "a pole's occupancy changed", """UPDATE neutral_operator.field_pole_occupancies SET "Version" = "Version" + 1 WHERE "PoleFeatureId" = 'pole-a'""" },
        { "kernel", "a target was added to a moved order", """INSERT INTO public."ServiceOrderTargets" ("Id","ServiceOrderId","VerticalId","FeatureId","AssetTypeId","ActivityIdsJson","Status","ConflictLevel","AddedAt","AddedBy","OrganizationId","Version") VALUES ('t-late','no-created','kernel','pole-late','pole','[]','Pending',0,'2026-11-20T10:00:00Z','owner-admin','a0a0a0a0-0000-4000-8000-000000000382',1)""" },
        { "kernel", "a new NeutralOperator order was created in the kernel", ServiceOrder },
        { "target", "a copied order's title changed", """UPDATE public."ServiceOrders" SET "Title" = 'altered in the target' WHERE "Id" = 'no-closed'""" },
        { "target", "a copied order is missing", """DELETE FROM public."ServiceOrders" WHERE "Id" = 'no-created'""" },
        { "target", "a copied saga pole is missing", """DELETE FROM neutral_operator.occupancy_apply_saga_poles WHERE "PoleFeatureId" = 'pole-b'""" },
        { "target", "a copied ledger row was released", """UPDATE public."ServiceOrderTargets" SET "ReleasedAt" = '2026-11-20T10:00:00Z' WHERE "Id" = 't-parent-e'""" },
        { "target", "a copied action log entry was edited", """UPDATE public."OrderActionLogs" SET "Comment" = 'edited' WHERE "ServiceOrderId" = 'no-closed' AND "ActionCode" = 'Cancel'""" },
    };

    private static async Task<(string Kernel, string Target)> CopiedPairAsync()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();
        (await MoveTool.RunAsync(kernel, target, "copy", "--include-pending-outbox")).ExitCode.Should().Be(0);
        (await MoveTool.RunAsync(kernel, target, "verify")).ExitCode.Should().Be(0);
        return (kernel, target);
    }

    [Theory]
    [MemberData(nameof(AlterationsAfterTheCopy))]
    public async Task Remove_WhenARowWasAlteredAfterTheCopy_RefusesAndDeletesNothing(string database, string scenario, string sql)
    {
        var (kernel, target) = await CopiedPairAsync();
        await Sql.ExecAsync(string.Equals(database, "kernel", StringComparison.Ordinal) ? kernel : target, sql);
        var kernelBefore = await DatabaseState.CaptureAsync(kernel);
        var targetBefore = await DatabaseState.CaptureAsync(target);
        var ledgerBefore = await Sql.ColumnAsync(kernel, """SELECT "Id" || '|' || "VerticalId" FROM public."ServiceOrderTargets" ORDER BY "Id" """);

        var result = await MoveTool.RunAsync(kernel, target, "remove", "--confirm");

        result.ExitCode.Should().Be(DataMoveExitCodes.Refused, $"{scenario}: {result.Text}");
        result.Text.Should().Contain("REFUSAL").And.Contain("refused");
        (await DatabaseState.CaptureAsync(kernel)).Differences(kernelBefore).Should().BeEmpty($"{scenario}: the kernel's rows must all still be there");
        (await DatabaseState.CaptureAsync(target)).Differences(targetBefore).Should().BeEmpty($"{scenario}: remove never writes to the target");
        (await Sql.ColumnAsync(kernel, """SELECT "Id" || '|' || "VerticalId" FROM public."ServiceOrderTargets" ORDER BY "Id" """))
            .Should().Equal(ledgerBefore, "a refused remove re-keys no ledger row either");
    }

    [Fact]
    public async Task Remove_RefusesWithAnExplanationNamingTheCause_ForAKernelRowChangedAfterTheCopy()
    {
        var (kernel, target) = await CopiedPairAsync();
        await Sql.ExecAsync(kernel, """UPDATE public."ServiceOrders" SET "Title" = 'altered' WHERE "Id" = 'no-created'""");

        var result = await MoveTool.RunAsync(kernel, target, "remove", "--confirm");

        result.Text.Should().Contain("no-created", "the differing key is named");
        ReportRows.Of(result.Text, "public.ServiceOrders")[ReportRows.Differ].Should().Be(1);
        ReportRows.Total(result.Text, ReportRows.Removed).Should().Be(0);
    }

    [Fact]
    public async Task Remove_BeforeAnyCopy_RefusesAndDeletesNothing()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();
        var kernelBefore = await DatabaseState.CaptureAsync(kernel);

        var result = await MoveTool.RunAsync(kernel, target, "remove", "--confirm");

        result.ExitCode.Should().Be(3, result.Text);
        ReportRows.Of(result.Text, "public.ServiceOrders")[ReportRows.Missing].Should().Be(6);
        (await DatabaseState.CaptureAsync(kernel)).Differences(kernelBefore).Should().BeEmpty();
    }

    [Fact]
    public async Task Remove_WithoutConfirm_IsAUsageError_AndTouchesNothing()
    {
        var (kernel, target) = await CopiedPairAsync();
        var kernelBefore = await DatabaseState.CaptureAsync(kernel);

        var result = await MoveTool.RunAsync(kernel, target, "remove");

        result.ExitCode.Should().Be(DataMoveExitCodes.UsageError).And.Be(1);
        result.Error.Should().Contain("pass --confirm");
        (await DatabaseState.CaptureAsync(kernel)).Differences(kernelBefore).Should().BeEmpty();
        (await Sql.CountAsync(kernel, "public.\"ServiceOrders\"")).Should().Be(7);
    }

    [Fact]
    public async Task Remove_AfterAnOrderWasCreatedLate_RefusesUntilItIsCopied_ThenRemovesEverything()
    {
        var (kernel, target) = await CopiedPairAsync();
        await Sql.ExecAsync(kernel, ServiceOrder);

        (await MoveTool.RunAsync(kernel, target, "verify")).ExitCode.Should().Be(2, "the late order is missing from the target");
        (await MoveTool.RunAsync(kernel, target, "remove", "--confirm")).ExitCode.Should().Be(3);
        (await Sql.CountAsync(kernel, "public.\"ServiceOrders\"", "\"Id\" = 'no-late'")).Should().Be(1);

        (await MoveTool.RunAsync(kernel, target, "copy", "--include-pending-outbox")).ExitCode.Should().Be(0);
        var removed = await MoveTool.RunAsync(kernel, target, "remove", "--confirm");

        removed.ExitCode.Should().Be(0, removed.Text);
        (await Sql.CountAsync(kernel, "public.\"ServiceOrders\"")).Should().Be(1, "only the kernel's own order remains");
    }

    [Fact]
    public async Task Remove_RunTwice_IsANoOpTheSecondTime_ThatLeavesTheKernelAsTheFirstLeftIt()
    {
        var (kernel, target) = await CopiedPairAsync();
        (await MoveTool.RunAsync(kernel, target, "remove", "--confirm")).ExitCode.Should().Be(0);
        var kernelAfterFirst = await DatabaseState.CaptureAsync(kernel);

        var again = await MoveTool.RunAsync(kernel, target, "remove", "--confirm");

        again.ExitCode.Should().Be(0, again.Text);
        ReportRows.Total(again.Text, ReportRows.Removed).Should().Be(0);
        (await DatabaseState.CaptureAsync(kernel)).Differences(kernelAfterFirst).Should().BeEmpty("a second remove changes nothing");
        (await Sql.ColumnAsync(kernel, """SELECT "VerticalId" FROM public."ServiceOrderTargets" WHERE "Id" = 't-approved-a'""")).Should().Equal("neutral-operator");
    }

    [Fact]
    public async Task Remove_RemovedRowsAreGoneFromTheKernel_ButTheTargetStillHoldsEveryOne()
    {
        var (kernel, target) = await CopiedPairAsync();
        var targetBefore = await DatabaseState.CaptureAsync(target);

        var result = await MoveTool.RunAsync(kernel, target, "remove", "--confirm");

        result.ExitCode.Should().Be(0, result.Text);
        foreach (var order in KernelSeed.Moved)
        {
            (await Sql.CountAsync(kernel, "public.\"ServiceOrders\"", $"\"Id\" = '{order}'")).Should().Be(0, order);
            (await Sql.CountAsync(target, "public.\"ServiceOrders\"", $"\"Id\" = '{order}'")).Should().Be(1, order);
        }

        (await Sql.CountAsync(kernel, "public.\"ServiceOrders\"", "\"Id\" = 'kernel-inspection'")).Should().Be(1);
        (await Sql.CountAsync(kernel, "public.\"OrderActionLogs\"", "\"ServiceOrderId\" = 'kernel-inspection'")).Should().Be(1);
        (await DatabaseState.CaptureAsync(target)).Differences(targetBefore).Should().BeEmpty();
        ReportRows.Of(result.Text, "public.ServiceOrders")[ReportRows.Removed].Should().Be(6);
        ReportRows.Of(result.Text, "public.ServiceOrderTargets")[ReportRows.Removed].Should().Be(5, "the moved orders' ledger rows are re-keyed, not deleted");
    }
}
