using System.Collections.Frozen;
using System.Globalization;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// How many discrete (vector-tier) features one vector tile holds (ADR-002 D6, XD01-457): at most
/// <see cref="MaxFeaturesPerTile"/> across its layers, and at most <see cref="ForLayer"/> of any one layer — so a dense
/// layer cannot spend the whole tile's budget and starve the sparse layers drawn with it (the first features by layer,
/// then ordinal id, of each layer, then of the tile). Aggregate layers are not counted: an aggregate is at most a grid of
/// points. A layer id is an asset type id in its <c>"D"</c> form, compared ordinally, as <see cref="ILodPolicy"/>'s.
/// Fails fast: the constructor refuses an invalid budget, so a host building it from configuration refuses to start.
/// </summary>
public sealed class TileFeatureBudget
{
    /// <summary>The default <see cref="MaxFeaturesPerTile"/>.</summary>
    public const int DefaultMaxFeaturesPerTile = 10_000;

    /// <summary>The largest budget a tile, or one of its layers, may be given.</summary>
    public const int Limit = 1_000_000;

    private readonly FrozenDictionary<string, int> _layers;

    /// <param name="maxFeaturesPerTile">The tile's budget, across its layers.</param>
    /// <param name="maxFeaturesPerLayer">Each layer's budget within the tile; <c>null</c> = the tile's (no layer cap: the
    /// first layers may spend it all).</param>
    /// <param name="layerBudgets">Per layer, its own budget within the tile, instead of <paramref name="maxFeaturesPerLayer"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">A budget is not in [1, <see cref="Limit"/>], or a layer's exceeds the tile's.</exception>
    /// <exception cref="ArgumentException">A layer id is blank.</exception>
    public TileFeatureBudget(int maxFeaturesPerTile = DefaultMaxFeaturesPerTile, int? maxFeaturesPerLayer = null, IReadOnlyDictionary<string, int>? layerBudgets = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFeaturesPerTile);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxFeaturesPerTile, Limit);
        if (maxFeaturesPerLayer is { } perLayer)
            EnsureLayerBudget(perLayer, maxFeaturesPerTile, nameof(maxFeaturesPerLayer));

        var layers = layerBudgets ?? FrozenDictionary<string, int>.Empty;
        foreach (var (layerId, budget) in layers)
        {
            if (string.IsNullOrWhiteSpace(layerId))
                throw new ArgumentException("A layer id must not be blank.", nameof(layerBudgets));
            EnsureLayerBudget(budget, maxFeaturesPerTile, $"{nameof(layerBudgets)}[{layerId}]");
        }

        MaxFeaturesPerTile = maxFeaturesPerTile;
        MaxFeaturesPerLayer = maxFeaturesPerLayer ?? maxFeaturesPerTile;
        _layers = layers.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>A tile budget of <see cref="DefaultMaxFeaturesPerTile"/>, no layer cap.</summary>
    public static TileFeatureBudget Default { get; } = new();

    /// <summary>The most discrete features one tile holds, across its layers.</summary>
    public int MaxFeaturesPerTile { get; }

    /// <summary>The most discrete features of one layer a tile holds, for a layer without its own budget (at most <see cref="MaxFeaturesPerTile"/>).</summary>
    public int MaxFeaturesPerLayer { get; }

    /// <summary>The layers with their own budget.</summary>
    public IReadOnlyDictionary<string, int> Layers => _layers;

    /// <summary>The most features of <paramref name="layerId"/> a tile holds: its own budget, else <see cref="MaxFeaturesPerLayer"/>.</summary>
    public int ForLayer(string layerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        return _layers.TryGetValue(layerId, out var budget) ? budget : MaxFeaturesPerLayer;
    }

    private static void EnsureLayerBudget(int budget, int maxFeaturesPerTile, string name)
    {
        if (budget < 1 || budget > maxFeaturesPerTile)
            throw new ArgumentOutOfRangeException(
                name, budget, string.Create(CultureInfo.InvariantCulture, $"A layer's budget must be between 1 and the tile's ({maxFeaturesPerTile:N0})."));
    }
}
