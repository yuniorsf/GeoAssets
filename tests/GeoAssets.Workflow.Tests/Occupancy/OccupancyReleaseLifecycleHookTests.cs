using FluentAssertions;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Occupancy;

public class OccupancyReleaseLifecycleHookTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    private sealed class RecordingCloser : IOccupancyChangeSetCloser
    {
        public List<(string OrderId, DateTimeOffset ClosedAt)> Calls { get; } = [];

        public Task DiscardStagedAsync(string orderId, DateTimeOffset closedAt, CancellationToken cancellationToken)
        {
            Calls.Add((orderId, closedAt));
            return Task.CompletedTask;
        }
    }

    private static OrderTransitionContext Context(string to) =>
        new(new ServiceOrder { Id = "ord-7" }, null, ServiceOrderStatus.InProgress, to, At);

    [Theory]
    [InlineData(ServiceOrderStatus.Completed)]
    [InlineData(ServiceOrderStatus.Cancelled)]
    public async Task Terminal_DiscardsStagedSetsOfTheOrderOnce_AtTheTransitionInstant(string to)
    {
        var closer = new RecordingCloser();

        await new OccupancyReleaseLifecycleHook(closer).OnTransitionedAsync(Context(to), CancellationToken.None);

        closer.Calls.Should().Equal(("ord-7", At));
    }

    [Fact]
    public async Task NonTerminal_DoesNotDiscard()
    {
        var closer = new RecordingCloser();

        await new OccupancyReleaseLifecycleHook(closer).OnTransitionedAsync(Context(ServiceOrderStatus.OnHold), CancellationToken.None);

        closer.Calls.Should().BeEmpty();
    }

    [Fact]
    public Task NullContext_Throws()
    {
        var act = () => new OccupancyReleaseLifecycleHook(new RecordingCloser()).OnTransitionedAsync(null!, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }
}
