using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

/// <summary>
/// XD01-229's AC: "Outbox messages are delivered at-least-once with idempotent apply
/// (re-delivery doesn't double-mutate)" — a redelivered <c>BatchId</c> must return the original
/// result without re-running <see cref="AssetMutationBatchApplier"/>.
/// </summary>
public class IdempotentMutationBatchApplierTests
{
    private static readonly Guid Org = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeAppliedMutationBatchStore : IAppliedMutationBatchStore
    {
        private readonly Dictionary<Guid, AssetBatchAppliedEvent> _applied = [];
        public int RecordCallCount { get; private set; }

        public Task<AssetBatchAppliedEvent?> FindAsync(Guid batchId, CancellationToken ct = default) =>
            Task.FromResult(_applied.GetValueOrDefault(batchId));

        public Task RecordAsync(AssetBatchAppliedEvent applied, CancellationToken ct = default)
        {
            RecordCallCount++;
            _applied[applied.BatchId] = applied;
            return Task.CompletedTask;
        }
    }

    private static AssetMutationBatchRequestedEvent Batch(Guid batchId, string itemCode, decimal quantity = 1m) =>
        AssetMutationBatchRequestedEvent.Create(
            catalogInstanceId: "catalog-primary",
            organizationId: Org,
            requestedByService: "GeoAssets.FieldService.Server",
            performedByUserId: "inspector-1",
            mutations:
            [
                new AssetFeatureMutationItem(
                    itemCode, AssetMutationKind.UpsertFeature, "Name", "pole", "", "",
                    Geometry: null, TopologyEdges: [], CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal)),
            ],
            timeProvider: new FakeTimeProvider(FixedNowUtc),
            batchId: batchId);

    [Fact]
    public async Task ApplyAsync_FirstDelivery_AppliesAndRecords()
    {
        var provider = new TestAssetProvider();
        var store = new FakeAppliedMutationBatchStore();
        var applier = new IdempotentMutationBatchApplier(provider, store, new FakeTimeProvider(FixedNowUtc));
        var batchId = Guid.NewGuid();

        await applier.ApplyAsync(Batch(batchId, "pole-1"));

        provider.GetById("pole-1").Should().NotBeNull();
        store.RecordCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ApplyAsync_RedeliveredBatchId_ReturnsOriginalResultWithoutReapplying()
    {
        var provider = new TestAssetProvider();
        var store = new FakeAppliedMutationBatchStore();
        var applier = new IdempotentMutationBatchApplier(provider, store, new FakeTimeProvider(FixedNowUtc));
        var batchId = Guid.NewGuid();

        var first = await applier.ApplyAsync(Batch(batchId, "pole-1"));

        // Simulate the feature having been subsequently modified by something else, to prove a
        // redelivery does NOT re-run the applier (which would stomp this change back).
        provider.GetById("pole-1")!.Properties.Name = "Modified after first apply";

        var second = await applier.ApplyAsync(Batch(batchId, "pole-1"));

        second.BatchId.Should().Be(first.BatchId);
        store.RecordCallCount.Should().Be(1, "RecordAsync must only run once, on the first delivery");
        provider.GetById("pole-1")!.Properties.Name.Should().Be("Modified after first apply", "re-delivery must not re-apply and stomp the later change");
    }

    [Fact]
    public async Task ApplyAsync_DifferentBatchIds_BothApply()
    {
        var provider = new TestAssetProvider();
        var store = new FakeAppliedMutationBatchStore();
        var applier = new IdempotentMutationBatchApplier(provider, store, new FakeTimeProvider(FixedNowUtc));

        await applier.ApplyAsync(Batch(Guid.NewGuid(), "pole-1"));
        await applier.ApplyAsync(Batch(Guid.NewGuid(), "pole-2"));

        provider.GetById("pole-1").Should().NotBeNull();
        provider.GetById("pole-2").Should().NotBeNull();
        store.RecordCallCount.Should().Be(2);
    }
}
