using System.Globalization;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// The tile endpoints' settings (XD01-434), from <c>GeoAssets:Tiles</c>; every value has a default, and an invalid one
/// fails the startup (ADR-002 D7).
/// </summary>
/// <param name="MaxCachedZoom">The highest zoom whose tiles the server keeps (<c>MaxCachedZoom</c>, default 18): above it
/// a tile is drawn per request (few features each), so a write evicts no deeper than this, and it is the lowest
/// <c>maxzoom</c> <c>style.json</c> gives its sources (a map client overzooms beyond).</param>
/// <param name="MaxEvictedTilesPerWrite">The most tiles one committed write evicts one by one (<c>MaxEvictedTilesPerWrite</c>,
/// default 65,536, counted over its regions and the zooms up to <paramref name="MaxCachedZoom"/>); a larger write (a bulk
/// import, a catalog load) flushes the whole cache by changing every layer version instead.</param>
/// <param name="CacheMaxBytes">The server tile cache's size bound (<c>CacheMaxMegabytes</c>, default 256 MiB).</param>
/// <param name="Lod">The level-of-detail policy (<c>Lod:RasterFromZoom</c>/<c>Lod:VectorFromZoom</c>, per asset type under
/// <c>Lod:Layers:{assetTypeId}</c>); vector at every zoom by default — the tile sources' behaviour without a policy.</param>
public sealed record TileServingOptions(int MaxCachedZoom, int MaxEvictedTilesPerWrite, long CacheMaxBytes, LodPolicy Lod)
{
    /// <summary>The configuration section.</summary>
    public const string SectionKey = "GeoAssets:Tiles";

    public const int DefaultMaxCachedZoom = 18;

    public const int DefaultMaxEvictedTilesPerWrite = 65_536;

    public const long DefaultCacheMaxMegabytes = 256;

    /// <summary>The defaults.</summary>
    public static TileServingOptions Default { get; } = new(
        DefaultMaxCachedZoom, DefaultMaxEvictedTilesPerWrite, DefaultCacheMaxMegabytes * 1024 * 1024, new LodPolicy(LodThresholds.VectorOnly));

    /// <summary>The options of <paramref name="configuration"/>'s <see cref="SectionKey"/> section, defaults for what it leaves out.</summary>
    /// <exception cref="InvalidOperationException">A value is not a number or is out of range.</exception>
    /// <exception cref="InvalidLodPolicyException">The LOD thresholds are invalid.</exception>
    public static TileServingOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(SectionKey);

        var maxCachedZoom = Read(section, "MaxCachedZoom", DefaultMaxCachedZoom);
        if (maxCachedZoom is < TileAddress.MinZoom or > TileAddress.MaxZoom)
            throw Invalid(section, "MaxCachedZoom", string.Create(CultureInfo.InvariantCulture, $"a zoom in [{TileAddress.MinZoom}, {TileAddress.MaxZoom}]"));

        var maxEvicted = Read(section, "MaxEvictedTilesPerWrite", DefaultMaxEvictedTilesPerWrite);
        if (maxEvicted < 1)
            throw Invalid(section, "MaxEvictedTilesPerWrite", "a positive number of tiles");

        var cacheMegabytes = Read(section, "CacheMaxMegabytes", DefaultCacheMaxMegabytes);
        if (cacheMegabytes is < 1 or > long.MaxValue / (1024 * 1024))
            throw Invalid(section, "CacheMaxMegabytes", "a positive number of megabytes");

        var lod = section.GetSection("Lod");
        var layers = lod.GetSection("Layers").GetChildren().ToDictionary(
            layer => layer.Key,
            layer => Thresholds(layer, defaults: null),
            StringComparer.Ordinal);

        return new TileServingOptions(maxCachedZoom, maxEvicted, cacheMegabytes * 1024 * 1024, new LodPolicy(Thresholds(lod, LodThresholds.VectorOnly), layers));
    }

    private static LodThresholds Thresholds(IConfigurationSection section, LodThresholds? defaults) =>
        new(
            Read(section, "RasterFromZoom", defaults?.RasterFromZoom ?? 0),
            Read(section, "VectorFromZoom", defaults?.VectorFromZoom ?? 0),
            ReadOptional(section, "MinZoom"),
            ReadOptional(section, "MaxZoom"));

    private static int Read(IConfigurationSection section, string key, int fallback) =>
        ReadOptional(section, key) ?? fallback;

    private static long Read(IConfigurationSection section, string key, long fallback)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Invalid(section, key, "a whole number");
    }

    private static int? ReadOptional(IConfigurationSection section, string key)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Invalid(section, key, "a whole number");
    }

    private static InvalidOperationException Invalid(IConfigurationSection section, string key, string expected) =>
        new($"'{section.Path}:{key}' must be {expected}; it is '{section[key]}'.");
}
