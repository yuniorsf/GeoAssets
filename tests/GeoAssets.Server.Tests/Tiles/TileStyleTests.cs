using System.Globalization;
using System.Text.Json.Nodes;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Server.Tiles;
using Xunit;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// XD01-434 — <see cref="TileLayerSetDescriptor"/> (which asset types a <c>{layer}</c> names, in what order, at which
/// tier, and the style hash in <c>{v}</c>) and <see cref="TileStyleDocument"/> (<c>style.json</c>: MapLibre v8 sources
/// carrying <c>{v}</c>, <c>promoteId</c>, LOD zoom bounds, and a color expression that paints every feature as
/// <see cref="RenderStyle"/> / <c>LayerResolver</c> — the raster and WMS colors — does).
/// </summary>
public sealed class TileStyleTests
{
    private const string Base = "https://maps.example.com";
    private const string Version = "0123456789ab-cafe0001-0.4";

    private static readonly AssetType Poles = new() { Id = Guid.Parse("00000000-0000-0000-0000-0000000000a1"), Name = "Poles", Color = "#111111", AllowedGeometryType = GeometryType.Point, MinZoom = 15 };
    private static readonly AssetType Feeders = new() { Id = Guid.Parse("00000000-0000-0000-0000-0000000000a2"), Name = "Feeders", Color = "#222222", AllowedGeometryType = GeometryType.LineString };
    private static readonly AssetType Districts = new() { Id = Guid.Parse("00000000-0000-0000-0000-0000000000a3"), Name = "Districts", Color = "#333333", AllowedGeometryType = GeometryType.Polygon, MinZoom = 8, MaxZoom = 13 };
    private static readonly AssetType Anything = new() { Id = Guid.Parse("00000000-0000-0000-0000-0000000000a4"), Name = "Anything", Color = "#444444" };

    private static readonly LodPolicy VectorOnly = new(LodThresholds.VectorOnly);

    private static TestAssetProvider Catalog() => new([Poles, Feeders, Districts, Anything]);

    // ── Layer sets ────────────────────────────────────────────────────────────

    [Fact]
    public void Assets_IsEveryVisibleType_PolygonsLinesAnyPoints()
    {
        var set = TileLayerSetDescriptor.Resolve("assets", Catalog(), VectorOnly)!;

        set.AssetTypes.Should().Equal(Districts, Feeders, Anything, Poles);
        set.Id.Should().Be("assets");
    }

    [Fact]
    public void AnAssetTypeId_IsThatTypeAlone_AndAnUnknownIdIsNoSet()
    {
        var catalog = Catalog();

        TileLayerSetDescriptor.Resolve(TileLayerSetDescriptor.LayerIdOf(Feeders), catalog, VectorOnly)!.AssetTypes.Should().Equal(Feeders);
        TileLayerSetDescriptor.Resolve(Guid.NewGuid().ToString(), catalog, VectorOnly).Should().BeNull();
        TileLayerSetDescriptor.Resolve("ASSETS", catalog, VectorOnly).Should().BeNull("set ids are ordinal");
    }

    [Fact]
    public void LayerSetAt_GivesVectorTilesTheVectorAndAggregateTiers_AndRasterTilesTheRasterTier_WithinEachTypesZoomRange()
    {
        var lod = new LodPolicy(new LodThresholds(RasterFromZoom: 6, VectorFromZoom: 12));
        var set = TileLayerSetDescriptor.Resolve("assets", Catalog(), lod)!;

        set.LayerSetAt(TileFormat.Mvt, 4)!.LayerIds.Should().Equal(new[] { Id(Feeders), Id(Anything) }, "aggregates below z6; districts start at 8, poles at 15");
        set.LayerSetAt(TileFormat.Png, 4).Should().BeNull();
        set.LayerSetAt(TileFormat.Png, 9)!.LayerIds.Should().Equal(Id(Districts), Id(Feeders), Id(Anything));
        set.LayerSetAt(TileFormat.Mvt, 9).Should().BeNull("every type visible at 9 is raster there");
        set.LayerSetAt(TileFormat.Mvt, 16)!.LayerIds.Should().Equal(Id(Feeders), Id(Anything), Id(Poles));
        set.LayerSetAt(TileFormat.Png, 16).Should().BeNull();
    }

    [Fact]
    public void TheStyleHash_ChangesWithWhatTilesHold_AndOnlyThen()
    {
        var catalog = Catalog();
        var before = TileLayerSetDescriptor.Resolve("assets", catalog, VectorOnly)!.StyleHash;

        TileLayerSetDescriptor.Resolve("assets", catalog, VectorOnly)!.StyleHash.Should().Be(before);
        before.Should().MatchRegex("^[0-9a-f]{12}$");
        TileLayerSetDescriptor.Resolve(Id(Poles), catalog, VectorOnly)!.StyleHash.Should().NotBe(before);
        TileLayerSetDescriptor.Resolve("assets", catalog, new LodPolicy(new LodThresholds(0, 10)))!.StyleHash.Should().NotBe(before);

        var recolored = Catalog();
        recolored.DeleteAssetType(Feeders.Id);
        recolored.AddAssetType(new AssetType { Id = Feeders.Id, Name = Feeders.Name, Color = "#ff0000", AllowedGeometryType = GeometryType.LineString });
        TileLayerSetDescriptor.Resolve("assets", recolored, VectorOnly)!.StyleHash.Should().NotBe(before);

        var ruled = Catalog();
        var layer = new Layer { Id = Guid.NewGuid(), Color = "#00ff00", GeometryType = GeometryType.Point };
        ruled.AddLayer(layer);
        TileLayerSetDescriptor.Resolve("assets", ruled, VectorOnly)!.StyleHash.Should().NotBe(before, "a layer is a feature override target");
        var withLayer = TileLayerSetDescriptor.Resolve("assets", ruled, VectorOnly)!.StyleHash;
        ruled.AddLayerRule(new LayerRule { AssetTypeId = Poles.Id, LayerId = layer.Id, Conditions = [new LayerRuleCondition { Attribute = "status", Value = "x" }] });
        TileLayerSetDescriptor.Resolve("assets", ruled, VectorOnly)!.StyleHash.Should().NotBe(withLayer);
    }

    // ── style.json ────────────────────────────────────────────────────────────

    [Fact]
    public void Style_IsMapLibreV8_WithAVectorSourceCarryingTheVersion_AndPromotingId()
    {
        var style = Build(VectorOnly);

        style["version"]!.GetValue<int>().Should().Be(8);
        style["metadata"]!["geoassets:version"]!.GetValue<string>().Should().Be(Version);
        style["metadata"]!["geoassets:layerSet"]!.GetValue<string>().Should().Be("assets");
        var vector = style["sources"]![TileStyleDocument.VectorSourceId]!;
        vector["type"]!.GetValue<string>().Should().Be("vector");
        vector["tiles"]!.AsArray().Select(t => t!.GetValue<string>()).Should().Equal($"{Base}/tiles/assets/{Version}/{{z}}/{{x}}/{{y}}.mvt");
        vector["promoteId"]!.GetValue<string>().Should().Be("id");
        vector["minzoom"]!.GetValue<int>().Should().Be(0);
        vector["maxzoom"]!.GetValue<int>().Should().Be(18);
        style["sources"]![TileStyleDocument.RasterSourceId].Should().BeNull("no layer has a raster tier under a vector-only policy");
        style["glyphs"].Should().BeNull();
    }

    [Fact]
    public void Style_HasEachTypesLayers_ForItsGeometry_WithinItsZoomRange_PolygonsBelowLinesBelowPoints()
    {
        var layers = Layers(Build(VectorOnly));

        layers.Select(l => (string)l["id"]!).Should().Equal(
            $"{Id(Districts)}-fill", $"{Id(Anything)}-fill",
            $"{Id(Districts)}-outline", $"{Id(Anything)}-outline",
            $"{Id(Feeders)}-line", $"{Id(Anything)}-line",
            $"{Id(Anything)}-point", $"{Id(Poles)}-point");

        var districts = LayerNamed(layers, $"{Id(Districts)}-fill");
        districts["source"]!.GetValue<string>().Should().Be(TileStyleDocument.VectorSourceId);
        districts["source-layer"]!.GetValue<string>().Should().Be(Id(Districts));
        districts["minzoom"]!.GetValue<int>().Should().Be(8);
        districts["maxzoom"]!.GetValue<int>().Should().Be(14, "maxzoom is exclusive: the type's last zoom is 13");
        var poles = LayerNamed(layers, $"{Id(Poles)}-point");
        poles["minzoom"]!.GetValue<int>().Should().Be(15);
        poles["maxzoom"].Should().BeNull("an open-ended type is drawn to the top zoom");
        LayerNamed(layers, $"{Id(Feeders)}-line")["minzoom"].Should().BeNull();
    }

    [Fact]
    public void Style_WithAThreeTierPolicy_HasARasterSourceAndLayer_AndAggregates_EachOverItsZooms()
    {
        var style = Build(new LodPolicy(new LodThresholds(RasterFromZoom: 6, VectorFromZoom: 12)));
        var layers = Layers(style);

        var raster = style["sources"]![TileStyleDocument.RasterSourceId]!;
        raster["type"]!.GetValue<string>().Should().Be("raster");
        raster["tileSize"]!.GetValue<int>().Should().Be(256);
        raster["tiles"]!.AsArray().Single()!.GetValue<string>().Should().Be($"{Base}/tiles/assets/{Version}/{{z}}/{{x}}/{{y}}.png");
        var rasterLayer = layers[0];
        rasterLayer["id"]!.GetValue<string>().Should().Be(TileStyleDocument.RasterLayerId, "the raster tier is drawn below every vector layer");
        rasterLayer["minzoom"]!.GetValue<int>().Should().Be(6);
        rasterLayer["maxzoom"]!.GetValue<int>().Should().Be(12);

        var aggregate = LayerNamed(layers, $"{Id(Feeders)}-aggregate");
        aggregate["maxzoom"]!.GetValue<int>().Should().Be(6);
        aggregate["filter"]!.ToJsonString().Should().Be("""["has","count"]""");
        layers.Should().NotContain(l => string.Equals((string)l["id"]!, $"{Id(Districts)}-aggregate", StringComparison.Ordinal), "districts start at 8, above the aggregate tier");
        LayerNamed(layers, $"{Id(Feeders)}-line")["minzoom"]!.GetValue<int>().Should().Be(12);
    }

    [Fact]
    public void Style_SourceMaxZoom_RisesToTheHighestFirstZoom_SoEveryTypeIsInSomeTile()
    {
        var catalog = new TestAssetProvider([new AssetType { Id = Guid.NewGuid(), Name = "Deep", MinZoom = 21 }]);

        var style = TileStyleDocument.Build(TileLayerSetDescriptor.Resolve("assets", catalog, VectorOnly)!, Version, Base, maxCachedZoom: 18);

        style["sources"]![TileStyleDocument.VectorSourceId]!["maxzoom"]!.GetValue<int>().Should().Be(21);
    }

    [Fact]
    public void ColorExpression_PaintsEveryFeature_AsLayerResolverDoes()
    {
        var catalog = Catalog();
        var overrideLayer = new Layer { Id = Guid.NewGuid(), Color = "#0000aa", GeometryType = GeometryType.Point };
        var activeLayer = new Layer { Id = Guid.NewGuid(), Color = "#00aa00", GeometryType = GeometryType.Point };
        var tallLayer = new Layer { Id = Guid.NewGuid(), Color = "#aa0000", GeometryType = GeometryType.Point };
        var shortLayer = new Layer { Id = Guid.NewGuid(), Color = "#aaaa00", GeometryType = GeometryType.Point };
        var defaultLayer = new Layer { Id = Guid.NewGuid(), Color = "#00aaaa", GeometryType = GeometryType.Point };
        foreach (var layer in new[] { overrideLayer, activeLayer, tallLayer, shortLayer, defaultLayer }) catalog.AddLayer(layer);
        catalog.DeleteAssetType(Poles.Id);
        var poles = new AssetType { Id = Poles.Id, Name = Poles.Name, Color = Poles.Color, AllowedGeometryType = GeometryType.Point, DefaultLayerId = defaultLayer.Id };
        catalog.AddAssetType(poles);
        catalog.AddLayerRule(new LayerRule
        {
            AssetTypeId = poles.Id, LayerId = tallLayer.Id, Priority = 2,
            Conditions = [new LayerRuleCondition { Attribute = "height", Operator = LayerRuleOperator.GreaterThanOrEqual, Value = "12" }],
        });
        catalog.AddLayerRule(new LayerRule
        {
            AssetTypeId = poles.Id, LayerId = activeLayer.Id, Priority = 1,
            Conditions =
            [
                new LayerRuleCondition { Attribute = "status", Value = "active" },
                new LayerRuleCondition { Attribute = "owner", Operator = LayerRuleOperator.NotEquals, Value = "third-party" },
            ],
        });
        catalog.AddLayerRule(new LayerRule
        {
            AssetTypeId = poles.Id, LayerId = shortLayer.Id, Priority = 3,
            Conditions = [new LayerRuleCondition { Attribute = "height", Operator = LayerRuleOperator.LessThanOrEqual, Value = "8.5" }],
        });
        catalog.AddLayerRule(new LayerRule
        {
            AssetTypeId = poles.Id, LayerId = Guid.NewGuid(), Priority = 4,
            Conditions = [new LayerRuleCondition { Attribute = "status", Value = "gone" }],
        });

        var set = TileLayerSetDescriptor.Resolve("assets", catalog, VectorOnly)!;
        var paint = LayerNamed(Layers(TileStyleDocument.Build(set, Version, Base, 18)), $"{Id(poles)}-point")["paint"]!;
        var expression = paint["circle-color"]!;
        paint["circle-stroke-color"]!.ToJsonString().Should().Be(expression.ToJsonString());
        var style = RenderStyle.Of(catalog);

        var cases = new (string? LayerId, Dictionary<string, string> Attributes)[]
        {
            (null, Attrs()),
            (overrideLayer.Id.ToString(), Attrs(("status", "active"))),
            (Guid.NewGuid().ToString(), Attrs()),
            (null, Attrs(("status", "active"))),
            (null, Attrs(("status", "active"), ("owner", "us"))),
            (null, Attrs(("status", "active"), ("owner", "third-party"), ("height", "15"))),
            (null, Attrs(("height", "12"))),
            (null, Attrs(("height", "11.99"))),
            (null, Attrs(("height", "8.5"))),
            (null, Attrs(("height", " 7 "))),
            (null, Attrs(("height", "tall"))),
            (null, Attrs(("height", string.Empty))),
            (null, Attrs(("height", "1e3"))),
            (null, Attrs(("status", "gone"), ("height", "20"))),
            (null, Attrs(("status", "Active"))),
        };

        foreach (var (layerId, attributes) in cases)
        {
            var feature = new GeoFeature
            {
                Id = "f",
                Properties = new GeoFeatureProperties { AssetTypeId = Id(poles), LayerId = layerId ?? string.Empty, CustomAttributes = attributes },
            };
            var properties = attributes.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            if (layerId is not null) properties["layerId"] = layerId;

            Evaluate(expression, properties).Should().Be(
                style.ColorOf(feature),
                "layerId={0} attributes={1}",
                layerId ?? "(none)",
                string.Join(",", attributes.Select(kv => $"{kv.Key}={kv.Value}")));
        }
    }

    [Fact]
    public void ColorExpression_WithoutLayersOrRules_IsTheTypesColor()
    {
        var paint = LayerNamed(Layers(Build(VectorOnly)), $"{Id(Feeders)}-line")["paint"]!;

        paint["line-color"]!.GetValue<string>().Should().Be("#222222");
        paint["line-width"]!.GetValue<double>().Should().Be(2);
    }

    private static JsonObject Build(ILodPolicy lod) =>
        TileStyleDocument.Build(TileLayerSetDescriptor.Resolve("assets", Catalog(), lod)!, Version, Base, maxCachedZoom: 18);

    private static JsonObject LayerNamed(List<JsonObject> layers, string id) =>
        layers.Single(l => string.Equals((string)l["id"]!, id, StringComparison.Ordinal));

    private static Dictionary<string, string> Attrs(params (string Key, string Value)[] attributes) =>
        attributes.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);

    private static List<JsonObject> Layers(JsonObject style) => [.. style["layers"]!.AsArray().Select(l => l!.AsObject())];

    private static string Id(AssetType type) => TileLayerSetDescriptor.LayerIdOf(type);

    // ── A MapLibre expression evaluator for the operators the style uses ──────

    private static object? Evaluate(JsonNode? node, IReadOnlyDictionary<string, string> properties)
    {
        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var s)) return s;
            if (value.TryGetValue<bool>(out var b)) return b;
            if (value.TryGetValue<int>(out var i)) return (double)i;
            return value.GetValue<double>();
        }

        var array = node!.AsArray();
        var op = array[0]!.GetValue<string>();
        object? Arg(int i) => Evaluate(array[i], properties);
        switch (op)
        {
            case "get": return properties.TryGetValue((string)Arg(1)!, out var v) ? v : null;
            case "has": return properties.ContainsKey((string)Arg(1)!);
            case "to-string": return Arg(1) switch { null => string.Empty, var x => Convert.ToString(x, CultureInfo.InvariantCulture) };
            case "==": return Equals(Arg(1), Arg(2));
            case "!=": return !Equals(Arg(1), Arg(2));
            case ">=": return (double)Arg(1)! >= (double)Arg(2)!;
            case "<=": return (double)Arg(1)! <= (double)Arg(2)!;
            case "all": return Enumerable.Range(1, array.Count - 1).All(i => (bool)Arg(i)!);
            case "to-number":
                for (var i = 1; i < array.Count; i++)
                {
                    if (ToNumber(Arg(i)) is { } number) return number;
                }

                throw new InvalidOperationException("to-number: nothing converts");
            case "case":
                for (var i = 1; i + 1 < array.Count; i += 2)
                {
                    if ((bool)Arg(i)!) return Arg(i + 1);
                }

                return Arg(array.Count - 1);
            case "match":
                var input = Arg(1);
                for (var i = 2; i + 1 < array.Count; i += 2)
                {
                    if (Equals(Arg(i), input)) return Arg(i + 1);
                }

                return Arg(array.Count - 1);
            default: throw new NotSupportedException(op);
        }
    }

    /// <summary>JavaScript's <c>Number(string)</c> for decimal inputs: trimmed, empty is 0, otherwise a decimal or NaN.</summary>
    private static double? ToNumber(object? value) => value switch
    {
        double d => d,
        string s when s.Trim().Length == 0 => 0,
        string s => double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null,
        _ => null,
    };
}
