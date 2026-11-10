using FluentAssertions;
using GeoAssets.Core.Erp.Maximo;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Core.Tests.Erp.Maximo;

/// <summary>
/// XD01-232's AC1 ("exactly one Maximo state update per serial pair") and AC2 ("failure to reach
/// Maximo retries without corrupting local ledger/outbox state").
/// </summary>
public class MaximoAssetLifecycleSyncServiceTests
{
    private static readonly Guid Org = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed class FakeDispatcher : IMaximoAssetLifecycleDispatcher
    {
        private Exception? _throwOn;
        public int CallCount { get; private set; }

        public FakeDispatcher(Exception? throwOn = null) => _throwOn = throwOn;

        public Task DispatchAsync(MaximoAssetLifecycleUpdate update, CancellationToken ct = default)
        {
            CallCount++;
            if (_throwOn is not null)
            {
                var toThrow = _throwOn;
                _throwOn = null; // only fail once, so a follow-up retry in the same test can succeed.
                throw toThrow;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryIdempotentInbox : IIdempotentInbox
    {
        private readonly HashSet<(Guid MessageId, string ConsumerName)> _processed = [];

        public Task<bool> HasProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default) =>
            Task.FromResult(_processed.Contains((messageId, consumerName)));

        public Task<bool> TryRecordProcessedAsync(
            Guid messageId, string consumerName, string messageType, TimeSpan retentionPeriod, CancellationToken ct = default) =>
            Task.FromResult(_processed.Add((messageId, consumerName)));

        public Task<int> PurgeExpiredAsync(int maxBatchSize, CancellationToken ct = default) => Task.FromResult(0);
    }

    private static AssetFeatureMutationItem Item(string featureId, AssetMutationKind kind, string serial) => new(
        FeatureId: featureId,
        MutationKind: kind,
        Name: "feature",
        AssetTypeId: "pole",
        Description: "",
        LayerId: "",
        Geometry: null,
        TopologyEdges: [],
        CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal) { ["serialNumber"] = serial });

    private static AssetMutationBatchRequestedEvent SwapBatch(TimeProvider timeProvider) => AssetMutationBatchRequestedEvent.Create(
        catalogInstanceId: "catalog-primary",
        organizationId: Org,
        requestedByService: "GeoAssets.FieldService.Server",
        performedByUserId: "inspector-1",
        mutations: [
            Item("feature-old", AssetMutationKind.Decommission, "SN-OLD"),
            Item("feature-new", AssetMutationKind.UpsertFeature, "SN-NEW"),
        ],
        timeProvider: timeProvider);

    private static AssetMutationBatchRequestedEvent NonSwapBatch(TimeProvider timeProvider) => AssetMutationBatchRequestedEvent.Create(
        catalogInstanceId: "catalog-primary",
        organizationId: Org,
        requestedByService: "GeoAssets.FieldService.Server",
        performedByUserId: "inspector-1",
        mutations: [],
        timeProvider: timeProvider);

    [Fact]
    public async Task SyncIfSwapAsync_NotASwap_ReturnsNotASwapAndNeverDispatches()
    {
        var dispatcher = new FakeDispatcher();
        var service = new MaximoAssetLifecycleSyncService(dispatcher, new InMemoryIdempotentInbox(), new FakeTimeProvider());

        var outcome = await service.SyncIfSwapAsync(NonSwapBatch(new FakeTimeProvider()));

        outcome.Should().Be(MaximoSyncOutcome.NotASwap);
        dispatcher.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SyncIfSwapAsync_ValidSwapNotYetProcessed_DispatchesOnceAndRecordsProcessed()
    {
        var dispatcher = new FakeDispatcher();
        var inbox = new InMemoryIdempotentInbox();
        var service = new MaximoAssetLifecycleSyncService(dispatcher, inbox, new FakeTimeProvider());
        var batch = SwapBatch(new FakeTimeProvider());

        var outcome = await service.SyncIfSwapAsync(batch);

        outcome.Should().Be(MaximoSyncOutcome.Dispatched);
        dispatcher.CallCount.Should().Be(1);
        (await inbox.HasProcessedAsync(batch.BatchId, MaximoAssetLifecycleSyncService.ConsumerName)).Should().BeTrue();
    }

    [Fact]
    public async Task SyncIfSwapAsync_CalledTwiceForSameBatch_DispatchesOnlyOnce()
    {
        var dispatcher = new FakeDispatcher();
        var service = new MaximoAssetLifecycleSyncService(dispatcher, new InMemoryIdempotentInbox(), new FakeTimeProvider());
        var batch = SwapBatch(new FakeTimeProvider());

        var first = await service.SyncIfSwapAsync(batch);
        var second = await service.SyncIfSwapAsync(batch);

        first.Should().Be(MaximoSyncOutcome.Dispatched);
        second.Should().Be(MaximoSyncOutcome.AlreadyDispatched);
        dispatcher.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task SyncIfSwapAsync_DispatchFails_DoesNotRecordProcessed_AndRetrySucceeds()
    {
        var dispatcher = new FakeDispatcher(throwOn: new HttpRequestException("Maximo MIF unreachable"));
        var inbox = new InMemoryIdempotentInbox();
        var service = new MaximoAssetLifecycleSyncService(dispatcher, inbox, new FakeTimeProvider());
        var batch = SwapBatch(new FakeTimeProvider());

        var failingAttempt = async () => await service.SyncIfSwapAsync(batch);
        await failingAttempt.Should().ThrowAsync<HttpRequestException>();

        dispatcher.CallCount.Should().Be(1, "the first attempt was made and failed");
        (await inbox.HasProcessedAsync(batch.BatchId, MaximoAssetLifecycleSyncService.ConsumerName)).Should().BeFalse(
            "a failed dispatch must not be recorded as processed, or a legitimate retry would be skipped forever");

        var retryOutcome = await service.SyncIfSwapAsync(batch);

        retryOutcome.Should().Be(MaximoSyncOutcome.Dispatched);
        dispatcher.CallCount.Should().Be(2, "the retry made a second, successful attempt");
        (await inbox.HasProcessedAsync(batch.BatchId, MaximoAssetLifecycleSyncService.ConsumerName)).Should().BeTrue();
    }
}
