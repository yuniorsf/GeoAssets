using FluentAssertions;
using GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests;

/// <summary>
/// The order of the runbook matters for a pending approval saga: once NeutralOperator.Server runs over the copy, its sweeper stamps a
/// saga still waiting past <c>Saga:ApplyTimeout</c> (<c>TimedOutAt</c>), which the kernel's copy of that saga does not carry — so
/// <c>verify</c> and <c>remove</c> then see the saga as differing. These tests pin that interplay so the runbook's ordering
/// (verify and remove before NeutralOperator.Server is started over the copy, or after its sweeper has been given time to settle) stays deliberate.
/// </summary>
public sealed class SagaSweeperInterplayTests
{
    [Fact]
    public async Task Verify_AfterNeutralOperatorServerStampedAPendingSagaOfTheCopy_ReportsTheSagaAsDifferent_AndRemoveRefuses()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();
        (await MoveTool.RunAsync(kernel, target, "copy", "--include-pending-outbox")).ExitCode.Should().Be(0);
        (await MoveTool.RunAsync(kernel, target, "verify")).ExitCode.Should().Be(0, "before the host runs, the copy is exact");

        var host = NeutralOperatorServerHost.Start(target, sagaApplyTimeout: "00:00:00.001", sweepInterval: "00:00:01");
        await using (host)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            while ((await Sql.CountAsync(target, "neutral_operator.occupancy_apply_sagas", "\"TimedOutAt\" IS NOT NULL")) == 0)
                await Task.Delay(TimeSpan.FromMilliseconds(250), TimeProvider.System, cancellation.Token);
        }

        var verify = await MoveTool.RunAsync(kernel, target, "verify");
        var remove = await MoveTool.RunAsync(kernel, target, "remove", "--confirm");

        ReportRows.Of(verify.Text, "neutral_operator.occupancy_apply_sagas")[ReportRows.Differ].Should().Be(1);
        verify.ExitCode.Should().Be(2, verify.Text);
        remove.ExitCode.Should().Be(3, remove.Text);
        (await Sql.CountAsync(kernel, "public.\"ServiceOrders\"")).Should().Be(7, "nothing was removed");
    }
}
