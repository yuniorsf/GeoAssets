using System.Collections.Frozen;

namespace GeoAssets.Core.Rendering;

/// <summary>
/// An <see cref="ILodPolicy"/> over fixed thresholds (XD01-431): per-layer <see cref="LodThresholds"/> (layer ids
/// compared ordinally), the <see cref="Default"/> ones for any other layer. Fails fast (ADR-002 D7): the constructor
/// throws <see cref="InvalidLodPolicyException"/> on the first invalid thresholds, so a host building it from
/// configuration at startup refuses to serve with a broken policy.
/// </summary>
public sealed class LodPolicy : ILodPolicy
{
    private readonly FrozenDictionary<string, LodThresholds> _layers;

    /// <summary>A policy of <paramref name="defaultThresholds"/>, overridden per layer by <paramref name="layerThresholds"/>.</summary>
    /// <exception cref="ArgumentException">A layer id is blank.</exception>
    /// <exception cref="InvalidLodPolicyException">The default or a layer's thresholds are invalid.</exception>
    public LodPolicy(LodThresholds defaultThresholds, IReadOnlyDictionary<string, LodThresholds>? layerThresholds = null)
    {
        defaultThresholds.EnsureValid();

        var layers = layerThresholds ?? FrozenDictionary<string, LodThresholds>.Empty;
        foreach (var (layerId, thresholds) in layers)
        {
            if (string.IsNullOrWhiteSpace(layerId))
                throw new ArgumentException("A layer id must not be blank.", nameof(layerThresholds));
            thresholds.EnsureValid(layerId);
        }

        Default = defaultThresholds;
        _layers = layers.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>The thresholds of every layer without its own.</summary>
    public LodThresholds Default { get; }

    /// <summary>The layers with their own thresholds.</summary>
    public IReadOnlyDictionary<string, LodThresholds> Layers => _layers;

    /// <inheritdoc />
    public TileRenderTier Decide(string layerId, int zoom) => GetThresholds(layerId).Decide(zoom);

    /// <inheritdoc />
    public LodThresholds GetThresholds(string layerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerId);
        return _layers.TryGetValue(layerId, out var thresholds) ? thresholds : Default;
    }
}
