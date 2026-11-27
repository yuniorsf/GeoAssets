using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// A tile layer set as one caller sees it (XD01-434): the <c>{layer}</c> of a tile URL resolved against the caller's
/// catalog. <see cref="AllAssetTypes"/> (<c>assets</c>) is every asset type the caller's organization sees; an asset type
/// id the caller sees is that type alone. A layer id is the asset type id (<see cref="Guid"/> "D" form), as the tile sources
/// read it. The asset types are ordered bottom first — polygons, lines, types of any geometry, points; then name and id —
/// which is the raster drawing order and the order the feature budget is spent in.
/// </summary>
public sealed class TileLayerSetDescriptor
{
    /// <summary>The layer set of every asset type the caller sees.</summary>
    public const string AllAssetTypes = "assets";

    private const int StyleHashLength = 12;

    private TileLayerSetDescriptor(
        string id, IReadOnlyList<AssetType> assetTypes, IReadOnlyList<Layer> layers, IReadOnlyDictionary<Guid, IReadOnlyList<LayerRule>> rules, ILodPolicy lod)
    {
        Id = id;
        AssetTypes = assetTypes;
        Layers = layers;
        Rules = rules;
        Lod = lod;
        StyleHash = ComputeStyleHash();
    }

    /// <summary>The <c>{layer}</c> segment.</summary>
    public string Id { get; }

    /// <summary>The set's asset types, bottom first; may be empty (an <c>assets</c> set of a catalog without types).</summary>
    public IReadOnlyList<AssetType> AssetTypes { get; }

    /// <summary>Every layer (style) of the catalog — a feature's override may name any.</summary>
    public IReadOnlyList<Layer> Layers { get; }

    /// <summary>Each asset type's layer rules, in the order <see cref="Core.Services.LayerResolver"/> evaluates them (priority, then catalog order).</summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<LayerRule>> Rules { get; }

    /// <summary>The level-of-detail policy the tiles are cut by.</summary>
    public ILodPolicy Lod { get; }

    /// <summary>
    /// A hash of everything but the features that decides what the set's tiles hold — its id, its asset types (zoom range,
    /// color, default layer, geometry), every layer, its types' layer rules and their LOD thresholds — 12 lowercase hex
    /// digits; the <c>{style}</c> part of <see cref="TileVersions"/>.
    /// </summary>
    public string StyleHash { get; }

    /// <summary>
    /// The set <paramref name="id"/> names in <paramref name="catalog"/> (the caller's view), or <c>null</c> when it names
    /// none the caller sees.
    /// </summary>
    public static TileLayerSetDescriptor? Resolve(string id, IAssetProvider catalog, ILodPolicy lod)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(lod);

        var visible = catalog.GetAssetTypes();
        IEnumerable<AssetType> types;
        if (string.Equals(id, AllAssetTypes, StringComparison.Ordinal))
        {
            types = visible;
        }
        else
        {
            var one = visible.FirstOrDefault(t => string.Equals(LayerIdOf(t), id, StringComparison.Ordinal));
            if (one is null) return null;
            types = [one];
        }

        var ordered = types
            .OrderBy(t => DrawingRank(t.AllowedGeometryType))
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .ThenBy(t => t.Id)
            .ToList();
        var rules = ordered.ToDictionary(
            t => t.Id,
            t => (IReadOnlyList<LayerRule>)[.. catalog.GetLayerRules(t.Id).Where(r => r.AssetTypeId == t.Id).OrderBy(r => r.Priority)]);

        return new TileLayerSetDescriptor(id, ordered, catalog.GetLayers(), rules, lod);
    }

    /// <summary>The layer id of <paramref name="assetType"/> in tiles: its id, "D" form.</summary>
    public static string LayerIdOf(AssetType assetType)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        return assetType.Id.ToString("D", CultureInfo.InvariantCulture);
    }

    /// <summary>The tier <paramref name="assetType"/> is drawn as at <paramref name="zoom"/>: hidden outside its own zoom range, else the policy's.</summary>
    public TileRenderTier TierOf(AssetType assetType, int zoom)
    {
        ArgumentNullException.ThrowIfNull(assetType);
        return AssetTypeZoomRange.Contains(assetType.MinZoom, assetType.MaxZoom, zoom)
            ? Lod.Decide(LayerIdOf(assetType), zoom)
            : TileRenderTier.Hidden;
    }

    /// <summary>
    /// The layers a <paramref name="format"/> tile at <paramref name="zoom"/> carries: vector tiles the
    /// <see cref="TileRenderTier.Vector"/> and <see cref="TileRenderTier.Aggregate"/> ones, raster tiles the
    /// <see cref="TileRenderTier.Raster"/> ones — so each tier's features travel once, in the format its zoom draws them in.
    /// </summary>
    public TileLayerSet? LayerSetAt(TileFormat format, int zoom)
    {
        var layers = AssetTypes
            .Where(t => TierOf(t, zoom) switch
            {
                TileRenderTier.Vector or TileRenderTier.Aggregate => format == TileFormat.Mvt,
                TileRenderTier.Raster => format == TileFormat.Png,
                _ => false,
            })
            .Select(LayerIdOf)
            .ToList();

        return layers.Count == 0 ? null : new TileLayerSet(Id, layers);
    }

    private static int DrawingRank(GeometryType? geometry) => geometry switch
    {
        GeometryType.Polygon => 0,
        GeometryType.LineString => 1,
        GeometryType.Point => 3,
        _ => 2,
    };

    private string ComputeStyleHash()
    {
        var text = new StringBuilder();
        Field(text, "set", Id);
        foreach (var type in AssetTypes)
        {
            var thresholds = Lod.GetThresholds(LayerIdOf(type));
            Field(text, "type", LayerIdOf(type), Number(type.MinZoom), Number(type.MaxZoom), type.Color,
                type.DefaultLayerId?.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty, type.AllowedGeometryType?.ToString() ?? string.Empty,
                Number(thresholds.RasterFromZoom), Number(thresholds.VectorFromZoom), Number(thresholds.MinZoom), Number(thresholds.MaxZoom));

            foreach (var rule in Rules[type.Id])
            {
                Field(text, "rule", rule.Id.ToString("D", CultureInfo.InvariantCulture), rule.LayerId.ToString("D", CultureInfo.InvariantCulture), Number(rule.Priority));
                foreach (var condition in rule.Conditions)
                    Field(text, "condition", condition.Attribute, condition.Operator.ToString(), condition.Value);
            }
        }

        foreach (var layer in Layers.OrderBy(l => l.Id))
        {
            Field(text, "layer", layer.Id.ToString("D", CultureInfo.InvariantCulture), layer.GeometryType.ToString(), layer.Color,
                Real(layer.Radius), Real(layer.Weight), layer.DashArray ?? string.Empty, layer.FillColor, Real(layer.FillOpacity));
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexStringLower(digest)[..StyleHashLength];
    }

    /// <summary>One length-prefixed record, so no two different inputs hash alike by concatenation.</summary>
    private static void Field(StringBuilder text, string kind, params string[] values)
    {
        text.Append(kind);
        foreach (var value in values)
            text.Append(CultureInfo.InvariantCulture, $"|{value.Length}:{value}");
        text.Append('\n');
    }

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "-";

    private static string Real(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
