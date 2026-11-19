using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

/// <summary>
/// XD01-374: the generic <see cref="IAssetMutationBatchApplier"/> a host uses when its catalog's provider registers no applier
/// of its own — plain <see cref="AssetMutationBatchApplier"/> without a store, <see cref="IdempotentMutationBatchApplier"/> with one.
/// </summary>
public class CatalogMutationBatchApplierTests
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

    private static AssetMutationBatchRequestedEvent Batch(Guid batchId, string itemCode) =>
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

    private static CatalogMutationBatchApplier Applier(IAssetProvider catalog, IAppliedMutationBatchStore? store) =>
        new(catalog, store, new FakeTimeProvider(FixedNowUtc));

    [Fact]
    public Task ApplyAsync_NullBatch_Throws()
    {
        var sut = Applier(new TestAssetProvider(), store: null);

        var act = () => sut.ApplyAsync(null!, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ApplyAsync_CancelledToken_ThrowsWithoutApplying()
    {
        var catalog = new TestAssetProvider();
        var store = new FakeAppliedMutationBatchStore();
        var sut = Applier(catalog, store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => sut.ApplyAsync(Batch(Guid.NewGuid(), "pole-1"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        catalog.GetById("pole-1").Should().BeNull();
        store.RecordCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ApplyAsync_WithoutStore_AppliesTheBatchToTheCatalog()
    {
        var catalog = new TestAssetProvider();
        var batchId = Guid.NewGuid();

        var result = await Applier(catalog, store: null).ApplyAsync(Batch(batchId, "pole-1"), CancellationToken.None);

        result.BatchId.Should().Be(batchId);
        catalog.GetById("pole-1").Should().NotBeNull();
    }

    [Fact]
    public async Task ApplyAsync_WithoutStore_ARedeliveredBatchIsAppliedAgain()
    {
        var catalog = new TestAssetProvider();
        var sut = Applier(catalog, store: null);
        var batchId = Guid.NewGuid();

        await sut.ApplyAsync(Batch(batchId, "pole-1"), CancellationToken.None);
        catalog.GetById("pole-1")!.Properties.Name = "Modified after first apply";
        await sut.ApplyAsync(Batch(batchId, "pole-1"), CancellationToken.None);

        catalog.GetById("pole-1")!.Properties.Name.Should().Be("Name");
    }

    [Fact]
    public async Task ApplyAsync_WithStore_AppliesAndRecordsTheBatch()
    {
        var catalog = new TestAssetProvider();
        var store = new FakeAppliedMutationBatchStore();
        var batchId = Guid.NewGuid();

        var result = await Applier(catalog, store).ApplyAsync(Batch(batchId, "pole-1"), CancellationToken.None);

        result.BatchId.Should().Be(batchId);
        catalog.GetById("pole-1").Should().NotBeNull();
        store.RecordCallCount.Should().Be(1);
        (await store.FindAsync(batchId)).Should().BeSameAs(result);
    }

    [Fact]
    public async Task ApplyAsync_WithStore_ARecordedBatchReturnsTheRecordedResultWithoutReapplying()
    {
        var catalog = new TestAssetProvider();
        var store = new FakeAppliedMutationBatchStore();
        var sut = Applier(catalog, store);
        var batchId = Guid.NewGuid();

        var first = await sut.ApplyAsync(Batch(batchId, "pole-1"), CancellationToken.None);
        catalog.GetById("pole-1")!.Properties.Name = "Modified after first apply";
        var second = await sut.ApplyAsync(Batch(batchId, "pole-1"), CancellationToken.None);

        second.Should().BeSameAs(first);
        store.RecordCallCount.Should().Be(1);
        catalog.GetById("pole-1")!.Properties.Name.Should().Be("Modified after first apply");
    }
}
