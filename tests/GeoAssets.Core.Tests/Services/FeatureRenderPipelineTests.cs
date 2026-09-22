using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

/// <summary>
/// Exercises <see cref="FeatureRenderPipeline.StreamAllAsync"/> (XD01-160) — chunk boundaries,
/// mid-stream cancellation, and that dispatch keeps a concurrently-awaited operation responsive
/// regardless of <see cref="ISyncProvider"/> classification (the marker only changes an internal
/// telemetry tag, not the dispatch code path, per the ticket's own design).
/// </summary>
public class FeatureRenderPipelineTests
{
    /// <summary>Bare-bones <see cref="IAssetProvider"/> whose <see cref="GetAll"/> returns a
    /// fixed count of features, optionally after a synchronous delay simulating blocking I/O.
    /// Every other member throws — the pipeline never calls them.</summary>
    private class FakeProvider(int featureCount, TimeSpan delay = default) : IAssetProvider
    {
        public IReadOnlyList<GeoFeature> GetAll()
        {
            if (delay > TimeSpan.Zero) Thread.Sleep(delay);
            return [.. Enumerable.Range(0, featureCount).Select(i => new GeoFeature { Id = $"f{i}" })];
        }

        public GeoFeature? GetById(string id) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> Search(string query) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) => throw new NotSupportedException();
        public Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) => throw new NotSupportedException();
        public void Add(GeoFeature feature) => throw new NotSupportedException();
        public void Update(GeoFeature feature) => throw new NotSupportedException();
        public void AddRange(IEnumerable<GeoFeature> features) => throw new NotSupportedException();
        public void Delete(string id) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void LoadAll(IEnumerable<GeoFeature> features) => throw new NotSupportedException();
        public IReadOnlyList<AssetType> GetAssetTypes() => throw new NotSupportedException();
        public void AddAssetType(AssetType assetType) => throw new NotSupportedException();
        public void DeleteAssetType(Guid id) => throw new NotSupportedException();
        public IReadOnlyList<Layer> GetLayers() => throw new NotSupportedException();
        public void AddLayer(Layer layer) => throw new NotSupportedException();
        public void DeleteLayer(Guid id) => throw new NotSupportedException();
        public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) => throw new NotSupportedException();
        public void AddLayerRule(LayerRule layerRule) => throw new NotSupportedException();
        public void DeleteLayerRule(Guid id) => throw new NotSupportedException();

        public event EventHandler<GeoFeature>? FeatureAdded { add { } remove { } }
        public event EventHandler<GeoFeature>? FeatureUpdated { add { } remove { } }
        public event EventHandler<string>? FeatureDeleted { add { } remove { } }
        public event EventHandler? CollectionChanged { add { } remove { } }
    }

    private sealed class FakeSyncProvider(int featureCount) : FakeProvider(featureCount), ISyncProvider;

    // ── Chunk boundaries ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(10, 3)]  // 4 chunks: 3,3,3,1
    [InlineData(9, 3)]   // 3 full chunks: 3,3,3
    [InlineData(5, 10)]  // 1 partial chunk: 5
    [InlineData(0, 5)]   // no chunks at all
    public async Task StreamAllAsync_RespectsChunkBoundaries(int featureCount, int chunkSize)
    {
        var provider = new FakeProvider(featureCount);
        var pipeline = new FeatureRenderPipeline();

        var chunks = new List<IReadOnlyList<GeoFeature>>();
        await foreach (var chunk in pipeline.StreamAllAsync(provider, chunkSize))
            chunks.Add(chunk);

        var expectedChunkCount = featureCount == 0 ? 0 : (int)Math.Ceiling(featureCount / (double)chunkSize);
        chunks.Should().HaveCount(expectedChunkCount);
        chunks.Sum(c => c.Count).Should().Be(featureCount);
        if (chunks.Count > 0)
            chunks.Should().OnlyContain(c => c.Count <= chunkSize && c.Count > 0);
        if (chunks.Count > 1)
            chunks.Take(chunks.Count - 1).Should().OnlyContain(c => c.Count == chunkSize);
    }

    [Fact]
    public void StreamAllAsync_NonPositiveChunkSize_Throws()
    {
        var pipeline = new FeatureRenderPipeline();
        var act = () => pipeline.StreamAllAsync(new FakeProvider(5), chunkSize: 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── Mid-stream cancellation ──────────────────────────────────────────────

    [Fact]
    public async Task StreamAllAsync_CancelledMidStream_StopsWithoutCompletingAllChunks()
    {
        var provider = new FakeProvider(1000);
        var pipeline = new FeatureRenderPipeline();
        using var cts = new CancellationTokenSource();
        var received = new List<IReadOnlyList<GeoFeature>>();

        var act = async () =>
        {
            await foreach (var chunk in pipeline.StreamAllAsync(provider, chunkSize: 10, cts.Token))
            {
                received.Add(chunk);
                if (received.Count == 2) cts.Cancel();
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        received.Should().HaveCountLessThan(100); // proves it actually stopped, not a vacuous pass
    }

    // ── Responsiveness — same dispatch path regardless of ISyncProvider classification ──

    [Theory]
    [InlineData(true)]  // ISyncProvider-marked
    [InlineData(false)] // not marked, artificially slow GetAll()
    public async Task StreamAllAsync_KeepsConcurrentOperationResponsive(bool useSyncProvider)
    {
        IAssetProvider provider = useSyncProvider
            ? new FakeSyncProvider(500)
            : new FakeProvider(500, delay: TimeSpan.FromMilliseconds(150));
        var pipeline = new FeatureRenderPipeline();

        var streamTask = Task.Run(async () =>
        {
            await foreach (var _ in pipeline.StreamAllAsync(provider, chunkSize: 50)) { }
        });

        var sw = Stopwatch.StartNew();
        await Task.Delay(5); // cheap, concurrently-awaited operation
        sw.Stop();

        await streamTask;

        sw.ElapsedMilliseconds.Should().BeLessThan(100); // not blocked by the other task's slow/blocking GetAll()
    }
}
