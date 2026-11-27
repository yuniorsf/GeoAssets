using System.Collections.Concurrent;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// A scripted <see cref="ITileDensitySampler"/> (XD01-439): every asset type has one seed; a measured tile of a type holds
/// <see cref="Density"/>'s features and vertices at its zoom (default: one feature of one vertex). Records what it is asked.
/// </summary>
internal sealed class ScriptedDensitySampler : ITileDensitySampler
{
    private readonly ConcurrentQueue<(string AssetTypeId, int Zoom)> _measured = new();

    /// <summary>Features and vertices per tile of an asset type at a zoom.</summary>
    public Func<string, int, (int Features, long Vertices)> Density { get; set; } = (_, _) => (1, 1);

    /// <summary>Thrown by every read when set.</summary>
    public Exception? Failure { get; set; }

    public IReadOnlyList<(string AssetTypeId, int Zoom)> Measured => [.. _measured];

    public Task<IReadOnlyList<TileDensitySeed>> SampleSeedsAsync(string assetTypeId, int maxSeeds, CancellationToken ct)
    {
        if (Failure is { } failure) return Task.FromException<IReadOnlyList<TileDensitySeed>>(failure);
        return Task.FromResult<IReadOnlyList<TileDensitySeed>>([new TileDensitySeed(-46.6, -23.5)]);
    }

    public Task<IReadOnlyList<TileDensitySample>> MeasureAsync(string assetTypeId, IReadOnlyList<TileAddress> tiles, int maxFeaturesPerTile, CancellationToken ct)
    {
        if (Failure is { } failure) return Task.FromException<IReadOnlyList<TileDensitySample>>(failure);

        _measured.Enqueue((assetTypeId, tiles[0].Z));
        var (features, vertices) = Density(assetTypeId, tiles[0].Z);
        return Task.FromResult<IReadOnlyList<TileDensitySample>>(
            [.. tiles.Select(t => new TileDensitySample(t, Math.Min(features, maxFeaturesPerTile), vertices, features >= maxFeaturesPerTile))]);
    }
}
