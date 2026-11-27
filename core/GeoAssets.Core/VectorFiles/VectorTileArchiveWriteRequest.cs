using System.Numerics;

namespace GeoAssets.Core.VectorFiles;

/// <summary>
/// How an <see cref="IVectorTileArchiveWriter"/> tiles the archive it writes (XD01-472): the zoom range, the MVT layer
/// of each asset type, the attributes and the tile geometry (extent, buffer, simplification).
/// </summary>
public sealed record VectorTileArchiveWriteRequest
{
    /// <summary>The lowest zoom when none is given.</summary>
    public const int DefaultMinZoom = 0;

    /// <summary>The highest zoom when none is given (street level, like most vector tilers' default).</summary>
    public const int DefaultMaxZoom = 14;

    /// <summary>The highest zoom an archive may hold (the PMTiles writer's limit).</summary>
    public const int MaxSupportedZoom = 22;

    /// <summary>The layer of a feature whose asset type id is blank.</summary>
    public const string UnclassifiedLayerName = "features";

    /// <summary>The tile extent (units per tile side) when none is given.</summary>
    public const int DefaultExtent = 4096;

    /// <summary>The smallest extent.</summary>
    public const int MinExtent = 256;

    /// <summary>The largest extent.</summary>
    public const int MaxExtent = 65_536;

    /// <summary>The buffer (tile units drawn past each tile edge) when none is given.</summary>
    public const int DefaultBuffer = 80;

    /// <summary>The largest buffer.</summary>
    public const int MaxBuffer = 4096;

    private readonly IReadOnlyDictionary<string, string>? _layerNames;
    private readonly int _extent = DefaultExtent;
    private readonly int _buffer = DefaultBuffer;
    private readonly double? _simplification;
    private readonly double? _simplificationMaxZoom;

    /// <summary>An archive of zooms <see cref="DefaultMinZoom"/> … <see cref="DefaultMaxZoom"/>.</summary>
    public VectorTileArchiveWriteRequest()
        : this(DefaultMinZoom, DefaultMaxZoom)
    {
    }

    /// <summary>An archive of zooms <paramref name="minZoom"/> … <paramref name="maxZoom"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A zoom is outside [0, <see cref="MaxSupportedZoom"/>], or <paramref name="minZoom"/> is above <paramref name="maxZoom"/>.
    /// </exception>
    public VectorTileArchiveWriteRequest(int minZoom, int maxZoom)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minZoom);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxZoom, MaxSupportedZoom);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minZoom, maxZoom);

        MinZoom = minZoom;
        MaxZoom = maxZoom;
    }

    /// <summary>The lowest zoom tiled.</summary>
    public int MinZoom { get; }

    /// <summary>The highest zoom tiled.</summary>
    public int MaxZoom { get; }

    /// <summary>
    /// The MVT layer name of each asset type id (<see cref="Models.GeoFeatureProperties.AssetTypeId"/>, matched
    /// exactly); <see langword="null"/> (the default), or an asset type it doesn't list, = a layer named after the asset
    /// type id itself. Two asset types may share a layer.
    /// </summary>
    /// <exception cref="ArgumentException">Set with a blank layer name.</exception>
    public IReadOnlyDictionary<string, string>? LayerNames
    {
        get => _layerNames;
        init
        {
            if (value is not null)
            {
                foreach (var (assetTypeId, layerName) in value)
                {
                    if (string.IsNullOrWhiteSpace(layerName))
                        throw new ArgumentException($"The layer name of asset type '{assetTypeId}' is blank.", nameof(value));
                }
            }

            _layerNames = value;
        }
    }

    /// <summary>
    /// The attributes written as tags; <see langword="null"/> (the default) = every
    /// <see cref="Models.GeoFeatureProperties.CustomAttributes"/> key a feature has. Given, a feature's other attributes
    /// are left out. Tags are per feature, so either way the input is written in one pass.
    /// </summary>
    public IReadOnlyList<string>? Attributes { get; init; }

    /// <summary>Tile units per tile side: a power of two within [<see cref="MinExtent"/>, <see cref="MaxExtent"/>].</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set outside that range, or not to a power of two.</exception>
    public int Extent
    {
        get => _extent;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinExtent);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxExtent);
            if (!BitOperations.IsPow2(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The extent must be a power of two.");
            _extent = value;
        }
    }

    /// <summary>Tile units drawn past each tile edge, so shapes crossing it join up: within [0, <see cref="MaxBuffer"/>].</summary>
    /// <exception cref="ArgumentOutOfRangeException">Set outside that range.</exception>
    public int Buffer
    {
        get => _buffer;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxBuffer);
            _buffer = value;
        }
    }

    /// <summary>
    /// The tolerance, in tile units, lines and polygons are simplified with below <see cref="MaxZoom"/>;
    /// <see langword="null"/> (the default) = the driver's own (no simplification).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative or non-finite value.</exception>
    public double? Simplification
    {
        get => _simplification;
        init => _simplification = NonNegative(value);
    }

    /// <summary>
    /// The tolerance, in tile units, at <see cref="MaxZoom"/>; <see langword="null"/> (the default) =
    /// <see cref="Simplification"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative or non-finite value.</exception>
    public double? SimplificationMaxZoom
    {
        get => _simplificationMaxZoom;
        init => _simplificationMaxZoom = NonNegative(value);
    }

    /// <summary>
    /// The MVT layer of a feature of asset type <paramref name="assetTypeId"/>: its <see cref="LayerNames"/> entry, else
    /// the id itself — <see cref="UnclassifiedLayerName"/> for a blank one.
    /// </summary>
    public string LayerNameOf(string? assetTypeId)
    {
        if (string.IsNullOrWhiteSpace(assetTypeId))
            return UnclassifiedLayerName;
        return _layerNames is not null && _layerNames.TryGetValue(assetTypeId, out var layerName) ? layerName : assetTypeId;
    }

    private static double? NonNegative(double? value)
    {
        if (value is { } tolerance && (!double.IsFinite(tolerance) || tolerance < 0))
            throw new ArgumentOutOfRangeException(nameof(value), value, "A simplification tolerance must be a finite, non-negative number.");
        return value;
    }
}
