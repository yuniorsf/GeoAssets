using FluentAssertions;
using GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests;

/// <summary>XD01-391 AC: running the copy twice changes nothing on the second run; a copy that stopped part-way resumes.</summary>
public sealed class CopyIdempotencyTests
{
    private const string WithOutbox = "--include-pending-outbox";

    [Fact]
    public async Task Copy_RunTwice_ChangesNothingOnTheSecondRun_NeitherDatabase()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();

        var first = await MoveTool.RunAsync(kernel, target, "copy", WithOutbox);
        var kernelAfterFirst = await DatabaseState.CaptureAsync(kernel);
        var targetAfterFirst = await DatabaseState.CaptureAsync(target);
        var second = await MoveTool.RunAsync(kernel, target, "copy", WithOutbox);

        first.ExitCode.Should().Be(0, first.Text);
        ReportRows.Total(first.Text, ReportRows.Copied).Should().Be(ReportRows.Total(first.Text, ReportRows.Source), "the first run copies every moved row");
        second.ExitCode.Should().Be(0, second.Text);
        second.Text.Should().Contain("nothing to copy (the target already holds every row)");
        ReportRows.Total(second.Text, ReportRows.Copied).Should().Be(0);
        ReportRows.Total(second.Text, ReportRows.Missing).Should().Be(0);
        ReportRows.Total(second.Text, ReportRows.Identical).Should().Be(ReportRows.Total(second.Text, ReportRows.Source));
        (await DatabaseState.CaptureAsync(kernel)).Differences(kernelAfterFirst).Should().BeEmpty();
        (await DatabaseState.CaptureAsync(target)).Differences(targetAfterFirst).Should().BeEmpty("row counts and content hashes of every table are unchanged");
        (await MoveTool.RunAsync(kernel, target, "verify")).ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task Copy_InSmallBatches_LandsTheSameRows_AndARerunInOneBatchChangesNothing()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();

        var batched = await MoveTool.RunAsync(kernel, target, "copy", WithOutbox, "--batch-size", "1");
        var afterBatched = await DatabaseState.CaptureAsync(target);
        var rerun = await MoveTool.RunAsync(kernel, target, "copy", WithOutbox);

        batched.ExitCode.Should().Be(0, batched.Text);
        ReportRows.Total(batched.Text, ReportRows.Copied).Should().Be(ReportRows.Total(batched.Text, ReportRows.Source));
        rerun.ExitCode.Should().Be(0, rerun.Text);
        ReportRows.Total(rerun.Text, ReportRows.Copied).Should().Be(0);
        (await DatabaseState.CaptureAsync(target)).Differences(afterBatched).Should().BeEmpty();
        (await MoveTool.RunAsync(kernel, target, "verify", "--batch-size", "2")).ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task Copy_AfterRowsWereLostFromTheTarget_CopiesOnlyWhatIsMissing_AndVerifyAgrees()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();
        (await MoveTool.RunAsync(kernel, target, "copy", WithOutbox)).ExitCode.Should().Be(0);
        await Sql.ExecAsync(target, """
            DELETE FROM neutral_operator."ServiceOrderOccupancyChangeSets" WHERE "Id" = 'cccccccc-0000-4000-8000-000000000003';
            DELETE FROM public."OrderActionLogs" WHERE "ServiceOrderId" IN ('no-approved', 'no-child');
            DELETE FROM public."OrderDispatches" WHERE "ServiceOrderId" = 'no-child';
            DELETE FROM public."ServiceOrders" WHERE "Id" = 'no-child';
            """);

        var resumed = await MoveTool.RunAsync(kernel, target, "copy", WithOutbox);

        resumed.ExitCode.Should().Be(0, resumed.Text);
        ReportRows.Of(resumed.Text, "public.ServiceOrders")[ReportRows.Copied].Should().Be(1);
        ReportRows.Of(resumed.Text, "public.OrderActionLogs")[ReportRows.Copied].Should().Be(4, "the three of no-approved and the one of no-child");
        ReportRows.Of(resumed.Text, "public.OrderDispatches")[ReportRows.Copied].Should().Be(1);
        ReportRows.Of(resumed.Text, "neutral_operator.ServiceOrderOccupancyChangeSets")[ReportRows.Copied].Should().Be(1);
        ReportRows.Of(resumed.Text, "public.ServiceOrderTargets")[ReportRows.Copied].Should().Be(0);
        (await MoveTool.RunAsync(kernel, target, "verify")).ExitCode.Should().Be(0);
        (await Sql.CountAsync(target, "public.\"OrderActionLogs\"")).Should().Be(9);
    }

    [Fact]
    public async Task Copy_KeepsTheStoredRowVersionOfEveryOrderByteForByte()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();

        (await MoveTool.RunAsync(kernel, target, "copy", WithOutbox)).ExitCode.Should().Be(0);

        const string versions = """SELECT "Id" || ':' || encode("RowVersion", 'hex') FROM public."ServiceOrders" WHERE "Id" LIKE 'no-%' ORDER BY "Id" """;
        var inKernel = await Sql.ColumnAsync(kernel, versions);
        inKernel.Should().HaveCount(6);
        (await Sql.ColumnAsync(target, versions)).Should().Equal(inKernel);
    }

    [Fact]
    public async Task Plan_ChangesNothingInEitherDatabase_AndAfterACopyReportsEverythingIdentical()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();
        var kernelBefore = await DatabaseState.CaptureAsync(kernel);
        var targetBefore = await DatabaseState.CaptureAsync(target);

        var plan = await MoveTool.RunAsync(kernel, target, "plan");

        plan.ExitCode.Should().Be(0, plan.Text);
        (await DatabaseState.CaptureAsync(kernel)).Differences(kernelBefore).Should().BeEmpty();
        (await DatabaseState.CaptureAsync(target)).Differences(targetBefore).Should().BeEmpty();

        await MoveTool.RunAsync(kernel, target, "copy", WithOutbox);
        var afterCopy = await MoveTool.RunAsync(kernel, target, "plan", WithOutbox);
        ReportRows.Total(afterCopy.Text, ReportRows.Identical).Should().Be(ReportRows.Total(afterCopy.Text, ReportRows.Source));
        ReportRows.Total(afterCopy.Text, ReportRows.Missing).Should().Be(0);
    }
}
