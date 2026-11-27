using System.Globalization;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// The tile endpoints' settings (XD01-434), from <c>GeoAssets:Tiles</c>; every value has a default, and an invalid one
/// fails the startup (ADR-002 D7) — read by <see cref="TileServiceExtensions.AddTileServing"/> while the host's services
/// are registered, so a broken section stops the host before it is built, let alone serves (XD01-439). Strict: a key the
/// section does not define (a typo such as <c>Lod:VectorFromZom</c>), a value given children, a layer id that is not an
/// asset type id, and every out-of-range or inverted zoom threshold is refused, naming the key.
/// </summary>
/// <param name="MaxCachedZoom">The highest zoom whose tiles the server keeps (<c>MaxCachedZoom</c>, default 18): above it
/// a tile is drawn per request (few features each), so a write evicts no deeper than this, and it is the lowest
/// <c>maxzoom</c> <c>style.json</c> gives its sources (a map client overzooms beyond).</param>
/// <param name="MaxEvictedTilesPerWrite">The most tiles one committed write evicts one by one (<c>MaxEvictedTilesPerWrite</c>,
/// default 65,536, counted over its regions and the zooms up to <paramref name="MaxCachedZoom"/>); a larger write (a bulk
/// import, a catalog load) flushes the whole cache by changing every layer version instead.</param>
/// <param name="CacheMaxBytes">The server tile cache's size bound (<c>CacheMaxMegabytes</c>, default 256 MiB).</param>
/// <param name="Lod">The level-of-detail policy (<c>Lod:RasterFromZoom</c>/<c>Lod:VectorFromZoom</c>/<c>Lod:MinZoom</c>/<c>Lod:MaxZoom</c>,
/// per asset type under <c>Lod:Layers:{assetTypeId}</c> with the same four keys); vector at every zoom by default — the tile
/// sources' behaviour without a policy.</param>
/// <param name="Calibration">The LOD calibration's sampling and budgets (<c>Calibration:*</c>, named as
/// <see cref="LodCalibrationSettings"/>'s properties, XD01-439).</param>
/// <param name="Budget">The vector tiles' discrete-feature budget (XD01-457): <c>Budget:MaxFeaturesPerTile</c> (default
/// 10,000, across a tile's layers), <c>Budget:MaxFeaturesPerLayer</c> (each layer's within a tile; default: the tile's, no
/// layer cap) and <c>Budget:Layers:{assetTypeId}</c> (a layer's own); no layer's may exceed the tile's.</param>
public sealed record TileServingOptions(
    int MaxCachedZoom, int MaxEvictedTilesPerWrite, long CacheMaxBytes, LodPolicy Lod, LodCalibrationSettings Calibration, TileFeatureBudget Budget)
{
    /// <summary>The configuration section.</summary>
    public const string SectionKey = "GeoAssets:Tiles";

    public const int DefaultMaxCachedZoom = 18;

    public const int DefaultMaxEvictedTilesPerWrite = 65_536;

    public const long DefaultCacheMaxMegabytes = 256;

    private const string LodKey = "Lod";

    private const string LayersKey = "Layers";

    private const string CalibrationKey = "Calibration";

    private const string BudgetKey = "Budget";

    private const string MaxFeaturesPerTileKey = "MaxFeaturesPerTile";

    private const string MaxFeaturesPerLayerKey = "MaxFeaturesPerLayer";

    private static readonly string[] SectionKeys = ["MaxCachedZoom", "MaxEvictedTilesPerWrite", "CacheMaxMegabytes", LodKey, CalibrationKey, BudgetKey];

    private static readonly string[] BudgetKeys = [MaxFeaturesPerTileKey, MaxFeaturesPerLayerKey, LayersKey];

    private static readonly string[] ThresholdKeys = ["RasterFromZoom", "VectorFromZoom", "MinZoom", "MaxZoom"];

    private static readonly string[] LodKeys = [.. ThresholdKeys, LayersKey];

    private static readonly string[] CalibrationKeys =
    [
        nameof(LodCalibrationSettings.MinZoom), nameof(LodCalibrationSettings.MaxZoom), nameof(LodCalibrationSettings.SeedFeatures),
        nameof(LodCalibrationSettings.SampleTiles), nameof(LodCalibrationSettings.MaxFeaturesPerTile),
        nameof(LodCalibrationSettings.VectorMaxFeaturesPerTile), nameof(LodCalibrationSettings.VectorMaxVerticesPerTile),
        nameof(LodCalibrationSettings.RasterMaxFeaturesPerTile), nameof(LodCalibrationSettings.RasterMaxVerticesPerTile),
    ];

    /// <summary>The defaults.</summary>
    public static TileServingOptions Default { get; } = new(
        DefaultMaxCachedZoom, DefaultMaxEvictedTilesPerWrite, DefaultCacheMaxMegabytes * 1024 * 1024, new LodPolicy(LodThresholds.VectorOnly),
        LodCalibrationSettings.Default, TileFeatureBudget.Default);

    /// <summary>The options of <paramref name="configuration"/>'s <see cref="SectionKey"/> section, defaults for what it leaves out.</summary>
    /// <exception cref="InvalidOperationException">A key is unknown, a value is not a number or is out of range, a layer id is
    /// not an asset type id, or the calibration settings or the feature budget are invalid.</exception>
    /// <exception cref="InvalidLodPolicyException">The LOD thresholds are invalid.</exception>
    public static TileServingOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(SectionKey);
        EnsureKnownKeys(section, SectionKeys);

        var maxCachedZoom = Read(section, "MaxCachedZoom", DefaultMaxCachedZoom);
        if (maxCachedZoom is < TileAddress.MinZoom or > TileAddress.MaxZoom)
            throw Invalid(section, "MaxCachedZoom", string.Create(CultureInfo.InvariantCulture, $"a zoom in [{TileAddress.MinZoom}, {TileAddress.MaxZoom}]"));

        var maxEvicted = Read(section, "MaxEvictedTilesPerWrite", DefaultMaxEvictedTilesPerWrite);
        if (maxEvicted < 1)
            throw Invalid(section, "MaxEvictedTilesPerWrite", "a positive number of tiles");

        var cacheMegabytes = Read(section, "CacheMaxMegabytes", DefaultCacheMaxMegabytes);
        if (cacheMegabytes is < 1 or > long.MaxValue / (1024 * 1024))
            throw Invalid(section, "CacheMaxMegabytes", "a positive number of megabytes");

        return new TileServingOptions(
            maxCachedZoom, maxEvicted, cacheMegabytes * 1024 * 1024, Policy(Section(section, LodKey)), CalibrationSettings(Section(section, CalibrationKey)),
            FeatureBudget(Section(section, BudgetKey)));
    }

    private static TileFeatureBudget FeatureBudget(IConfigurationSection budget)
    {
        EnsureKnownKeys(budget, BudgetKeys);

        var perTile = Read(budget, MaxFeaturesPerTileKey, TileFeatureBudget.DefaultMaxFeaturesPerTile);
        if (perTile is < 1 or > TileFeatureBudget.Limit)
            throw Invalid(budget, MaxFeaturesPerTileKey, string.Create(CultureInfo.InvariantCulture, $"a number of features in [1, {TileFeatureBudget.Limit}]"));

        var layerRange = string.Create(CultureInfo.InvariantCulture, $"a number of features in [1, {perTile}] (at most {MaxFeaturesPerTileKey})");
        var perLayer = ReadOptional(budget, MaxFeaturesPerLayerKey);
        if (perLayer is { } cap && (cap < 1 || cap > perTile))
            throw Invalid(budget, MaxFeaturesPerLayerKey, layerRange);

        var layers = new Dictionary<string, int>(StringComparer.Ordinal);
        var layersSection = Section(budget, LayersKey);
        foreach (var layer in layersSection.GetChildren())
        {
            if (!Guid.TryParseExact(layer.Key, "D", out var assetTypeId))
                throw new InvalidOperationException($"'{layer.Path}' must be named by an asset type id (a GUID such as 00000000-0000-0000-0000-000000000000); '{layer.Key}' is not one.");

            var layerBudget = ReadOptional(layersSection, layer.Key);
            if (layerBudget is not { } own || own < 1 || own > perTile)
                throw Invalid(layersSection, layer.Key, layerRange);
            layers.Add(assetTypeId.ToString("D", CultureInfo.InvariantCulture), own);
        }

        return new TileFeatureBudget(perTile, perLayer, layers);
    }

    private static LodPolicy Policy(IConfigurationSection lod)
    {
        EnsureKnownKeys(lod, LodKeys);

        var layers = new Dictionary<string, LodThresholds>(StringComparer.Ordinal);
        foreach (var layer in Section(lod, LayersKey).GetChildren())
        {
            // A layer id is an asset type id, which the tile sources compare ordinally in its "D" form (lowercase).
            if (!Guid.TryParseExact(layer.Key, "D", out var assetTypeId))
                throw new InvalidOperationException($"'{layer.Path}' must be named by an asset type id (a GUID such as 00000000-0000-0000-0000-000000000000); '{layer.Key}' is not one.");
            if (!string.IsNullOrEmpty(layer.Value))
                throw new InvalidOperationException($"'{layer.Path}' must be a section of {string.Join(", ", ThresholdKeys)}; it is the value '{layer.Value}'.");

            EnsureKnownKeys(layer, ThresholdKeys);
            layers.Add(assetTypeId.ToString("D", CultureInfo.InvariantCulture), Thresholds(layer, defaults: null));
        }

        return new LodPolicy(Thresholds(lod, LodThresholds.VectorOnly), layers);
    }

    private static LodCalibrationSettings CalibrationSettings(IConfigurationSection calibration)
    {
        EnsureKnownKeys(calibration, CalibrationKeys);

        var defaults = LodCalibrationSettings.Default;
        var settings = new LodCalibrationSettings
        {
            MinZoom = Read(calibration, nameof(LodCalibrationSettings.MinZoom), defaults.MinZoom),
            MaxZoom = Read(calibration, nameof(LodCalibrationSettings.MaxZoom), defaults.MaxZoom),
            SeedFeatures = Read(calibration, nameof(LodCalibrationSettings.SeedFeatures), defaults.SeedFeatures),
            SampleTiles = Read(calibration, nameof(LodCalibrationSettings.SampleTiles), defaults.SampleTiles),
            MaxFeaturesPerTile = Read(calibration, nameof(LodCalibrationSettings.MaxFeaturesPerTile), defaults.MaxFeaturesPerTile),
            VectorMaxFeaturesPerTile = Read(calibration, nameof(LodCalibrationSettings.VectorMaxFeaturesPerTile), defaults.VectorMaxFeaturesPerTile),
            VectorMaxVerticesPerTile = Read(calibration, nameof(LodCalibrationSettings.VectorMaxVerticesPerTile), defaults.VectorMaxVerticesPerTile),
            RasterMaxFeaturesPerTile = Read(calibration, nameof(LodCalibrationSettings.RasterMaxFeaturesPerTile), defaults.RasterMaxFeaturesPerTile),
            RasterMaxVerticesPerTile = Read(calibration, nameof(LodCalibrationSettings.RasterMaxVerticesPerTile), defaults.RasterMaxVerticesPerTile),
        };

        if (settings.FindDefect() is { } defect)
            throw new InvalidOperationException($"'{calibration.Path}' is invalid: {defect}");
        return settings;
    }

    private static LodThresholds Thresholds(IConfigurationSection section, LodThresholds? defaults) =>
        new(
            Read(section, "RasterFromZoom", defaults?.RasterFromZoom ?? 0),
            Read(section, "VectorFromZoom", defaults?.VectorFromZoom ?? 0),
            ReadOptional(section, "MinZoom"),
            ReadOptional(section, "MaxZoom"));

    /// <summary>Refuses a child of <paramref name="section"/> it does not define (keys compare as configuration does, ignoring case).</summary>
    private static void EnsureKnownKeys(IConfigurationSection section, string[] known)
    {
        foreach (var child in section.GetChildren())
        {
            if (!known.Contains(child.Key, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"'{child.Path}' is not a setting of '{section.Path}'; its settings are {string.Join(", ", known)}.");
        }
    }

    private static int Read(IConfigurationSection section, string key, int fallback) =>
        ReadOptional(section, key) ?? fallback;

    private static long Read(IConfigurationSection section, string key, long fallback)
    {
        var raw = Scalar(section, key);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Invalid(section, key, "a whole number");
    }

    private static int? ReadOptional(IConfigurationSection section, string key)
    {
        var raw = Scalar(section, key);
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Invalid(section, key, "a whole number");
    }

    /// <summary>The section <paramref name="key"/>; refuses a value instead (e.g. <c>Lod</c> = <c>14</c>).</summary>
    private static IConfigurationSection Section(IConfigurationSection parent, string key)
    {
        var child = parent.GetSection(key);
        if (!string.IsNullOrEmpty(child.Value))
            throw new InvalidOperationException($"'{child.Path}' must be a section, not the value '{child.Value}'.");
        return child;
    }

    /// <summary>The value of <paramref name="key"/>; refuses one given children instead (e.g. <c>MaxCachedZoom:0</c>).</summary>
    private static string? Scalar(IConfigurationSection section, string key)
    {
        var child = section.GetSection(key);
        if (child.Value is null && child.GetChildren().Any())
            throw Invalid(section, key, "a value, not a section");
        return child.Value;
    }

    private static InvalidOperationException Invalid(IConfigurationSection section, string key, string expected) =>
        new($"'{section.Path}:{key}' must be {expected}; it is '{section[key]}'.");
}
