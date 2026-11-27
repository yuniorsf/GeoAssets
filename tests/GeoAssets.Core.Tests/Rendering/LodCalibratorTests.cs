using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

/// <summary>
/// XD01-439 — <see cref="LodCalibrator"/> over a scripted sampler: it samples seeds once, measures from the highest drawn
/// zoom down within the settings' and the type's zoom ranges, stops at the first zoom that fits no tier, caps each read,
/// and recommends what <see cref="LodCalibration.Recommend"/> says.
/// </summary>
public class LodCalibratorTests
{
    private static readonly LodCalibrationSettings Settings = new()
    {
        MinZoom = 0,
        MaxZoom = 16,
        SeedFeatures = 8,
        SampleTiles = 4,
        MaxFeaturesPerTile = 1_000,
        VectorMaxFeaturesPerTile = 100,
        VectorMaxVerticesPerTile = 1_000,
        RasterMaxFeaturesPerTile = 500,
        RasterMaxVerticesPerTile = 5_000,
    };

    [Fact]
    public async Task ItMeasuresFromTheHighestZoomDown_AndStopsAtTheFirstThatFitsNoTier()
    {
        // Each zoom below 16 holds four times the features of the one above: 16 → 4, 15 → 16, 14 → 64, 13 → 256, 12 → 1,024.
        var sampler = new ScriptedSampler(zoom => (int)Math.Pow(4, 17 - zoom), verticesPerFeature: 2);

        var calibration = await new LodCalibrator(sampler, Settings).CalibrateAsync("feeders", null, null, CancellationToken.None);

        calibration.Seeds.Should().Be(8);
        calibration.Densities.Select(d => d.Zoom).Should().Equal(16, 15, 14, 13, 12);
        sampler.MeasuredZooms.Should().Equal(16, 15, 14, 13, 12);
        sampler.SeedCalls.Should().Be(1);
        sampler.Caps.Should().OnlyContain(cap => cap == 1_000);
        calibration.Densities[^1].SaturatedTiles.Should().Be(calibration.Densities[^1].SampledTiles, "1,024 features reach the 1,000 cap");
        calibration.Recommended.Should().Be(new LodThresholds(13, 14), "64 features fit vector; 256 fit raster; 1,000+ fit neither");
    }

    [Fact]
    public async Task ItMeasuresOnlyTheZoomsTheTypeIsDrawnAt()
    {
        var sampler = new ScriptedSampler(_ => 1, verticesPerFeature: 1);

        var calibration = await new LodCalibrator(sampler, Settings).CalibrateAsync("late", typeMinZoom: 12, typeMaxZoom: 14, CancellationToken.None);

        sampler.MeasuredZooms.Should().Equal(14, 13, 12);
        calibration.Recommended.Should().Be(LodThresholds.VectorOnly);
    }

    [Fact]
    public async Task ATypeDrawnAtNoCalibratedZoom_IsNotMeasured()
    {
        var sampler = new ScriptedSampler(_ => 1, verticesPerFeature: 1);

        var calibration = await new LodCalibrator(sampler, Settings).CalibrateAsync("deep", typeMinZoom: 17, typeMaxZoom: null, CancellationToken.None);

        calibration.Should().Be(new AssetTypeLodCalibration("deep", 0, [], null));
        sampler.SeedCalls.Should().Be(0);
    }

    [Fact]
    public async Task ATypeWithoutFeaturesInScope_HasNoRecommendation()
    {
        var sampler = new ScriptedSampler(_ => 1, verticesPerFeature: 1, seeds: 0);

        var calibration = await new LodCalibrator(sampler, Settings).CalibrateAsync("empty", null, null, CancellationToken.None);

        calibration.Recommended.Should().BeNull();
        calibration.Densities.Should().BeEmpty();
        sampler.MeasuredZooms.Should().BeEmpty();
    }

    [Fact]
    public async Task ACancelledToken_StopsBeforeAnyRead()
    {
        var sampler = new ScriptedSampler(_ => 1, verticesPerFeature: 1);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = () => new LodCalibrator(sampler, Settings).CalibrateAsync("feeders", null, null, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sampler.SeedCalls.Should().Be(0);
    }

    [Fact]
    public async Task ATokenCancelledMidway_StopsAtTheNextZoom()
    {
        using var cancellation = new CancellationTokenSource();
        var sampler = new ScriptedSampler(_ => 1, verticesPerFeature: 1, onMeasure: async zoom =>
        {
            if (zoom == 15) await cancellation.CancelAsync();
        });

        var act = () => new LodCalibrator(sampler, Settings).CalibrateAsync("feeders", null, null, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        sampler.MeasuredZooms.Should().Equal(16, 15);
    }

    [Fact]
    public void InvalidSettings_AreRefused()
    {
        var act = () => new LodCalibrator(new ScriptedSampler(_ => 1, 1), Settings with { SampleTiles = 0 });

        act.Should().Throw<InvalidOperationException>().WithMessage("*SampleTiles*");
    }

    /// <summary>Seeds spread over a small area; each measured tile holds <c>features(zoom)</c> features (capped) of <c>verticesPerFeature</c> vertices.</summary>
    private sealed class ScriptedSampler(Func<int, int> features, long verticesPerFeature, int seeds = 8, Func<int, Task>? onMeasure = null) : ITileDensitySampler
    {
        public int SeedCalls { get; private set; }

        public List<int> MeasuredZooms { get; } = [];

        public List<int> Caps { get; } = [];

        public Task<IReadOnlyList<TileDensitySeed>> SampleSeedsAsync(string assetTypeId, int maxSeeds, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            SeedCalls++;
            IReadOnlyList<TileDensitySeed> sampled = [.. Enumerable.Range(0, Math.Min(seeds, maxSeeds)).Select(i => new TileDensitySeed(-46 + (i * 0.01), -23 + (i * 0.01)))];
            return Task.FromResult(sampled);
        }

        public async Task<IReadOnlyList<TileDensitySample>> MeasureAsync(string assetTypeId, IReadOnlyList<TileAddress> tiles, int maxFeaturesPerTile, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var zoom = tiles[0].Z;
            MeasuredZooms.Add(zoom);
            Caps.Add(maxFeaturesPerTile);
            if (onMeasure is not null) await onMeasure(zoom);

            var count = Math.Min(features(zoom), maxFeaturesPerTile);
            return [.. tiles.Select(t => new TileDensitySample(t, count, count * verticesPerFeature, count >= maxFeaturesPerTile))];
        }
    }
}
