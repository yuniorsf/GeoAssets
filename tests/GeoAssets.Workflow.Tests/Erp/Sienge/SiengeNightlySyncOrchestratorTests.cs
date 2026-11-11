using FluentAssertions;
using GeoAssets.Workflow.Erp.Sienge;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Erp.Sienge;

public class SiengeNightlySyncOrchestratorTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FakeSyncClient : ISiengeSyncClient
    {
        public IReadOnlyList<SiengePurchaseOrderRecord> PurchaseOrdersToReturn { get; set; } = [];
        public Exception? PostingFailure { get; set; }
        public Exception? PurchaseOrderFailure { get; set; }
        public List<SiengeCostCenterPostingLine> PushedLines { get; } = [];

        public Task PushCostCenterPostingsAsync(IReadOnlyList<SiengeCostCenterPostingLine> postings, CancellationToken ct = default)
        {
            if (PostingFailure is not null) return Task.FromException(PostingFailure);
            PushedLines.AddRange(postings);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SiengePurchaseOrderRecord>> PullPurchaseOrdersSinceAsync(DateTime sinceUtc, CancellationToken ct = default) =>
            PurchaseOrderFailure is not null ? Task.FromException<IReadOnlyList<SiengePurchaseOrderRecord>>(PurchaseOrderFailure) : Task.FromResult(PurchaseOrdersToReturn);
    }

    private sealed class FakeMachineHourSource(IReadOnlyList<MachineHourRecord> records) : IMachineHourSource
    {
        public Task<IReadOnlyList<MachineHourRecord>> GetMachineHoursSinceAsync(DateTime sinceUtc, CancellationToken ct = default) => Task.FromResult(records);
    }

    private sealed class FakeHaulingTicketSource(IReadOnlyList<HaulingTicketRecord> records) : IHaulingTicketSource
    {
        public Task<IReadOnlyList<HaulingTicketRecord>> GetHaulingTicketsSinceAsync(DateTime sinceUtc, CancellationToken ct = default) => Task.FromResult(records);
    }

    private sealed class FakeConsumptionSource(IReadOnlyList<FieldConsumptionRecord> consumption) : IFieldConsumptionSource
    {
        public Task<IReadOnlyList<FieldConsumptionRecord>> GetConsumptionSinceAsync(DateTime sinceUtc, CancellationToken ct = default) => Task.FromResult(consumption);
    }

    private static SiengeSyncRetryOptions FastRetryOptions() => new() { BaseBackoffInterval = TimeSpan.FromMilliseconds(1), MaxBackoffInterval = TimeSpan.FromMilliseconds(1), MaxAttempts = 1 };

    [Fact]
    public async Task RunAsync_AllOperationsSucceed_ReturnsCountsAndNoFailures()
    {
        var syncClient = new FakeSyncClient
        {
            PurchaseOrdersToReturn = [new SiengePurchaseOrderRecord("po-1", "cc-1", "asphalt", 100m)],
        };
        var machineHourSource = new FakeMachineHourSource([new MachineHourRecord("cc-1", "us-equip-1", 8m, Now)]);
        var haulingTicketSource = new FakeHaulingTicketSource([new HaulingTicketRecord("cc-1", "us-haul-1", 12m, Now)]);
        var consumptionSource = new FakeConsumptionSource([new FieldConsumptionRecord("cc-1", "asphalt", 100m)]);
        var orchestrator = new SiengeNightlySyncOrchestrator(
            syncClient, machineHourSource, haulingTicketSource, consumptionSource, FastRetryOptions(), new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)));

        var result = await orchestrator.RunAsync(Now.AddDays(-1));

        result.AllOperationsSucceeded.Should().BeTrue();
        result.CostCenterPostingLinesPushed.Should().Be(2);
        result.DiscrepancyFlags.Should().BeEmpty();
        syncClient.PushedLines.Should().HaveCount(2);
    }

    [Fact]
    public async Task RunAsync_PostingFailure_DoesNotBlockReconciliation()
    {
        var syncClient = new FakeSyncClient
        {
            PostingFailure = new InvalidOperationException("sienge down"),
            PurchaseOrdersToReturn = [new SiengePurchaseOrderRecord("po-1", "cc-1", "asphalt", 50m)],
        };
        var orchestrator = new SiengeNightlySyncOrchestrator(
            syncClient, new FakeMachineHourSource([]), new FakeHaulingTicketSource([]),
            new FakeConsumptionSource([new FieldConsumptionRecord("cc-1", "asphalt", 50m)]), FastRetryOptions(), new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)));

        var result = await orchestrator.RunAsync(Now.AddDays(-1));

        result.AllOperationsSucceeded.Should().BeFalse();
        result.CostCenterPostingFailure.Should().NotBeNull();
        result.ReconciliationFailure.Should().BeNull();
        result.DiscrepancyFlags.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_PurchaseOrderPullFailure_IsSurfacedWithEmptyFlags()
    {
        var syncClient = new FakeSyncClient { PurchaseOrderFailure = new InvalidOperationException("po feed down") };
        var orchestrator = new SiengeNightlySyncOrchestrator(
            syncClient, new FakeMachineHourSource([]), new FakeHaulingTicketSource([]), new FakeConsumptionSource([]), FastRetryOptions(), new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)));

        var result = await orchestrator.RunAsync(Now.AddDays(-1));

        result.ReconciliationFailure.Should().NotBeNull();
        result.DiscrepancyFlags.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_DiscrepancyDetected_IsReturnedInResult()
    {
        var syncClient = new FakeSyncClient
        {
            PurchaseOrdersToReturn = [new SiengePurchaseOrderRecord("po-1", "cc-1", "asphalt", 100m)],
        };
        var orchestrator = new SiengeNightlySyncOrchestrator(
            syncClient, new FakeMachineHourSource([]), new FakeHaulingTicketSource([]),
            new FakeConsumptionSource([new FieldConsumptionRecord("cc-1", "asphalt", 10m)]), FastRetryOptions(), new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)));

        var result = await orchestrator.RunAsync(Now.AddDays(-1));

        result.DiscrepancyFlags.Should().ContainSingle(f => f.MaterialCode == "asphalt" && f.CostCenterCode == "cc-1");
    }

    [Fact]
    public async Task RunAsync_PushesMappedLinesCorrectly()
    {
        var syncClient = new FakeSyncClient();
        var machineHourSource = new FakeMachineHourSource([new MachineHourRecord("cc-1", "us-equip-1", 8m, Now)]);
        var orchestrator = new SiengeNightlySyncOrchestrator(
            syncClient, machineHourSource, new FakeHaulingTicketSource([]), new FakeConsumptionSource([]), FastRetryOptions(), new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)));

        await orchestrator.RunAsync(Now.AddDays(-1));

        syncClient.PushedLines.Should().ContainSingle(l => l.CostCenterCode == "cc-1" && l.UsCode == "us-equip-1" && l.Quantity == 8m);
    }
}
