using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// <c>GET /tiles/style.json</c>'s body (XD01-434): a MapLibre style (version 8) for one <see cref="TileLayerSetDescriptor"/>,
/// the one place a map client learns tile URLs — and so their <c>{v}</c> — from.
/// <list type="bullet">
///   <item><b>Sources</b> (TileJSON inline): <see cref="VectorSourceId"/> (<c>type: vector</c>,
///   <c>tiles: [".../tiles/{set}/{v}/{z}/{x}/{y}.mvt"]</c>, <c>promoteId: "id"</c> — the MVT feature id is numeric, a
///   feature's GeoAssets id is its <c>id</c> property) and, when some layer has a raster tier, <see cref="RasterSourceId"/>
///   (<c>type: raster</c>, 256 px <c>.png</c> tiles). <c>minzoom</c> 0; <c>maxzoom</c>
///   <see cref="TileServingOptions.MaxCachedZoom"/>, raised to the first zoom of the highest-starting layer so every layer
///   is in some tile (the client overzooms past it).</item>
///   <item><b>Layers</b>, bottom first: the raster layer (over the zooms some layer is <see cref="TileRenderTier.Raster"/>),
///   then for each asset type (<c>source-layer</c> = its id) its fills, polygon outlines, lines, points and — at its
///   <see cref="TileRenderTier.Aggregate"/> zooms — its aggregate circles (sized by <c>count</c>). Each layer's
///   <c>minzoom</c>/<c>maxzoom</c> is the tier's zoom run (<see cref="ILodPolicy"/> within the type's own zoom range;
///   <c>maxzoom</c> is exclusive in the style spec, so it is the run's last zoom + 1, left out at the top zoom). A type
///   restricted to one geometry gets that geometry's layers only, others all three, each filtered by geometry type.</item>
///   <item><b>Colors</b> are the expression of <see cref="Core.Services.LayerResolver"/> over the properties the vector
///   tiles carry, so a feature is painted as the raster tiles and WMS paint it (<see cref="RenderStyle"/>): the feature's
///   <c>layerId</c> override (any existing layer), else the first of its type's rules by priority whose conditions all hold
///   (a rule whose layer is gone gives the default, as the resolver does), else the type's default layer, else its color.
///   Ordering conditions compare numbers only, as the resolver does (a non-numeric attribute never matches; MapLibre's
///   string-to-number reads hexadecimal too, the resolver does not). Paint follows <c>SkiaFeaturePainter</c>: 5 px points,
///   2 px lines, polygons filled at 35 % with a 1.5 px outline.</item>
///   <item><c>metadata</c>: <c>geoassets:layerSet</c> and <c>geoassets:version</c> (the <c>{v}</c> in every URL).</item>
/// </list>
/// No <c>glyphs</c>/<c>sprite</c>: no layer draws text or icons.
/// </summary>
public static class TileStyleDocument
{
    public const string VectorSourceId = "geoassets";

    public const string RasterSourceId = "geoassets-raster";

    public const string RasterLayerId = "geoassets-raster";

    /// <summary>The feature property MapLibre promotes to the feature id.</summary>
    public const string PromotedId = "id";

    private const double FillOpacity = 0.35;
    private const double OutlineWidth = 1.5;
    private const double LineWidth = 2;
    private const double PointRadius = 5;
    private const double PointOpacity = 0.78;
    private const double PointStrokeWidth = 1.5;
    private const double AggregateOpacity = 0.6;

    private static readonly string[] PolygonTypes = ["Polygon", "MultiPolygon"];
    private static readonly string[] LineTypes = ["LineString", "MultiLineString"];
    private static readonly string[] PointTypes = ["Point", "MultiPoint"];

    /// <summary>The style of <paramref name="layerSet"/> at <c>{v}</c> = <paramref name="version"/>, its tile URLs under <paramref name="baseUrl"/> (no trailing slash).</summary>
    public static JsonObject Build(TileLayerSetDescriptor layerSet, string version, string baseUrl, int maxCachedZoom)
    {
        ArgumentNullException.ThrowIfNull(layerSet);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(baseUrl);

        var types = layerSet.AssetTypes.Select(t => (Type: t, Runs: TierRuns.Of(layerSet, t))).ToList();
        var vectorRuns = types.SelectMany(t => new[] { t.Runs.Vector, t.Runs.Aggregate }).OfType<ZoomRun>().ToList();
        var rasterRuns = types.Select(t => t.Runs.Raster).OfType<ZoomRun>().ToList();
        var template = $"{baseUrl}/tiles/{layerSet.Id}/{version}/{{z}}/{{x}}/{{y}}";

        var sources = new JsonObject();
        var layers = new JsonArray();
        if (vectorRuns.Count > 0)
            sources[VectorSourceId] = Source("vector", template + ".mvt", SourceMaxZoom(vectorRuns, maxCachedZoom));

        if (rasterRuns.Count > 0)
        {
            var source = Source("raster", template + ".png", SourceMaxZoom(rasterRuns, maxCachedZoom));
            source.Remove("promoteId");
            source["tileSize"] = 256;
            sources[RasterSourceId] = source;

            var raster = new ZoomRun(rasterRuns.Min(r => r.Min), rasterRuns.Max(r => r.Max));
            layers.Add(Zoomed(new JsonObject { ["id"] = RasterLayerId, ["type"] = "raster", ["source"] = RasterSourceId }, raster));
        }

        var byKind = new List<JsonObject>[5];
        for (var kind = 0; kind < byKind.Length; kind++) byKind[kind] = [];
        foreach (var (type, runs) in types)
            AddTypeLayers(byKind, layerSet, type, runs);

        foreach (var layer in byKind.SelectMany(kind => kind))
            layers.Add(layer);

        return new JsonObject
        {
            ["version"] = 8,
            ["name"] = "GeoAssets",
            ["metadata"] = new JsonObject
            {
                ["geoassets:layerSet"] = layerSet.Id,
                ["geoassets:version"] = version,
            },
            ["sources"] = sources,
            ["layers"] = layers,
        };
    }

    private static JsonObject Source(string type, string tiles, int maxZoom) => new()
    {
        ["type"] = type,
        ["tiles"] = new JsonArray(tiles),
        ["scheme"] = "xyz",
        ["minzoom"] = TileAddress.MinZoom,
        ["maxzoom"] = maxZoom,
        ["promoteId"] = PromotedId,
    };

    /// <summary>The style layers of <paramref name="type"/>, into <paramref name="byKind"/>: fills, outlines, lines, points, aggregates.</summary>
    private static void AddTypeLayers(List<JsonObject>[] byKind, TileLayerSetDescriptor layerSet, AssetType type, TierRuns runs)
    {
        var layerId = TileLayerSetDescriptor.LayerIdOf(type);
        if (runs.Vector is { } vector)
        {
            var color = ColorExpression(layerSet, type);
            if (Draws(type, GeometryType.Polygon))
            {
                byKind[0].Add(VectorLayer($"{layerId}-fill", "fill", layerId, vector, PolygonTypes, new JsonObject
                {
                    ["fill-color"] = color.DeepClone(),
                    ["fill-opacity"] = FillOpacity,
                }));
                byKind[1].Add(VectorLayer($"{layerId}-outline", "line", layerId, vector, PolygonTypes, new JsonObject
                {
                    ["line-color"] = color.DeepClone(),
                    ["line-width"] = OutlineWidth,
                }));
            }

            if (Draws(type, GeometryType.LineString))
            {
                var line = VectorLayer($"{layerId}-line", "line", layerId, vector, LineTypes, new JsonObject
                {
                    ["line-color"] = color.DeepClone(),
                    ["line-width"] = LineWidth,
                });
                line["layout"] = new JsonObject { ["line-cap"] = "round", ["line-join"] = "round" };
                byKind[2].Add(line);
            }

            if (Draws(type, GeometryType.Point))
            {
                byKind[3].Add(VectorLayer($"{layerId}-point", "circle", layerId, vector, PointTypes, new JsonObject
                {
                    ["circle-radius"] = PointRadius,
                    ["circle-color"] = color.DeepClone(),
                    ["circle-opacity"] = PointOpacity,
                    ["circle-stroke-color"] = color.DeepClone(),
                    ["circle-stroke-width"] = PointStrokeWidth,
                }));
            }
        }

        if (runs.Aggregate is { } aggregate)
            byKind[4].Add(AggregateLayer(layerId, DefaultColor(layerSet, type), aggregate));
    }

    private static JsonObject AggregateLayer(string layerId, string color, ZoomRun run) =>
        Zoomed(new JsonObject
        {
            ["id"] = $"{layerId}-aggregate",
            ["type"] = "circle",
            ["source"] = VectorSourceId,
            ["source-layer"] = layerId,
            ["filter"] = new JsonArray("has", "count"),
            ["paint"] = new JsonObject
            {
                ["circle-radius"] = new JsonArray("interpolate", new JsonArray("linear"), new JsonArray("get", "count"), 1, 4, 100, 10, 10_000, 24),
                ["circle-color"] = color,
                ["circle-opacity"] = AggregateOpacity,
                ["circle-stroke-color"] = color,
                ["circle-stroke-width"] = 1,
            },
        }, run);

    /// <summary>
    /// The paint color of <paramref name="type"/>'s features: <see cref="Core.Services.LayerResolver"/> as a MapLibre
    /// expression (or a plain color when nothing varies).
    /// </summary>
    internal static JsonNode ColorExpression(TileLayerSetDescriptor layerSet, AssetType type)
    {
        var fallback = DefaultColor(layerSet, type);
        var rules = layerSet.Rules[type.Id];

        JsonNode byRule = fallback;
        if (rules.Count > 0)
        {
            var cases = new JsonArray("case");
            foreach (var rule in rules)
            {
                cases.Add(RuleCondition(rule));
                cases.Add(layerSet.Layers.FirstOrDefault(l => l.Id == rule.LayerId) is { } layer ? ColorOf(layer.Color) : fallback);
            }

            cases.Add(fallback);
            byRule = cases;
        }

        if (layerSet.Layers.Count == 0) return byRule;

        var match = new JsonArray("match", new JsonArray("to-string", new JsonArray("get", "layerId")));
        foreach (var layer in layerSet.Layers)
        {
            match.Add(layer.Id.ToString("D", CultureInfo.InvariantCulture));
            match.Add(ColorOf(layer.Color));
        }

        match.Add(byRule);
        return match;
    }

    /// <summary>The color of a <paramref name="type"/> feature no override or rule styles: its default layer's, else its own.</summary>
    internal static string DefaultColor(TileLayerSetDescriptor layerSet, AssetType type) =>
        type.DefaultLayerId is { } id && layerSet.Layers.FirstOrDefault(l => l.Id == id) is { } layer
            ? ColorOf(layer.Color)
            : ColorOf(type.Color);

    private static JsonNode RuleCondition(LayerRule rule) => rule.Conditions.Count switch
    {
        0 => true,
        1 => Condition(rule.Conditions[0]),
        _ => new JsonArray([JsonValue.Create("all"), .. rule.Conditions.Select(Condition)]),
    };

    private static JsonNode Condition(LayerRuleCondition condition)
    {
        var attribute = condition.Attribute;
        return condition.Operator switch
        {
            LayerRuleOperator.Equals => new JsonArray("==", Get(attribute), condition.Value),
            LayerRuleOperator.NotEquals => new JsonArray("all", new JsonArray("has", attribute), new JsonArray("!=", Get(attribute), condition.Value)),
            LayerRuleOperator.GreaterThanOrEqual => Numeric(attribute, ">=", condition.Value),
            LayerRuleOperator.LessThanOrEqual => Numeric(attribute, "<=", condition.Value),
            _ => false,
        };
    }

    /// <summary>
    /// <paramref name="attribute"/> compared with the number <paramref name="value"/> — only when the attribute is a
    /// number: <c>to-number</c> with two different fallbacks agrees only for a convertible value. A condition whose own
    /// value is not a finite number never matches.
    /// </summary>
    private static JsonNode Numeric(string attribute, string comparison, string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            return false;

        return new JsonArray(
            "all",
            new JsonArray("has", attribute),
            new JsonArray("!=", Get(attribute), ""),
            new JsonArray("==", new JsonArray("to-number", Get(attribute), 0), new JsonArray("to-number", Get(attribute), 1)),
            new JsonArray(comparison, new JsonArray("to-number", Get(attribute), 0), number));
    }

    private static JsonArray Get(string attribute) => new("get", attribute);

    private static string ColorOf(string? color) => string.IsNullOrWhiteSpace(color) ? RenderStyle.DefaultColor : color;

    private static bool Draws(AssetType type, GeometryType geometry) =>
        type.AllowedGeometryType is not { } allowed || allowed == GeometryType.Raw || allowed == geometry;

    private static JsonObject VectorLayer(string id, string kind, string sourceLayer, ZoomRun run, string[] geometryTypes, JsonObject paint) =>
        Zoomed(new JsonObject
        {
            ["id"] = id,
            ["type"] = kind,
            ["source"] = VectorSourceId,
            ["source-layer"] = sourceLayer,
            ["filter"] = new JsonArray(
                "all",
                new JsonArray("!", new JsonArray("has", "count")),
                new JsonArray("in", new JsonArray("geometry-type"), new JsonArray("literal", new JsonArray([.. geometryTypes.Select(t => JsonValue.Create(t))])))),
            ["paint"] = paint,
        }, run);

    private static JsonObject Zoomed(JsonObject layer, ZoomRun run)
    {
        if (run.Min > TileAddress.MinZoom) layer["minzoom"] = run.Min;
        if (run.Max < TileAddress.MaxZoom) layer["maxzoom"] = run.Max + 1;
        return layer;
    }

    private static int SourceMaxZoom(List<ZoomRun> runs, int maxCachedZoom) =>
        Math.Min(TileAddress.MaxZoom, Math.Max(maxCachedZoom, runs.Max(r => r.Min)));

    /// <summary>The zooms <c>Min</c>…<c>Max</c> (inclusive) of one tier.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ZoomRun(int Min, int Max);

    /// <summary>The zoom run of each tier of one asset type (tiers are monotonic in zoom, so each is one run).</summary>
    private sealed record TierRuns(ZoomRun? Aggregate, ZoomRun? Raster, ZoomRun? Vector)
    {
        public static TierRuns Of(TileLayerSetDescriptor layerSet, AssetType type)
        {
            ZoomRun? aggregate = null, raster = null, vector = null;
            for (var zoom = TileAddress.MinZoom; zoom <= TileAddress.MaxZoom; zoom++)
            {
                switch (layerSet.TierOf(type, zoom))
                {
                    case TileRenderTier.Aggregate: aggregate = Extend(aggregate, zoom); break;
                    case TileRenderTier.Raster: raster = Extend(raster, zoom); break;
                    case TileRenderTier.Vector: vector = Extend(vector, zoom); break;
                    default: break;
                }
            }

            return new TierRuns(aggregate, raster, vector);
        }

        private static ZoomRun Extend(ZoomRun? run, int zoom) => run is { } r ? r with { Max = zoom } : new ZoomRun(zoom, zoom);
    }
}
