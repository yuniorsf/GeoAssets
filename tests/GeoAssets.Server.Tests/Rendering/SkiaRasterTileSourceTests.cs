using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Server.Rendering;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.IO;
using SkiaSharp;
using Xunit;

namespace GeoAssets.Server.Tests.Rendering;

/// <summary>
/// <see cref="SkiaRasterTileSource"/> (XD01-433): EPSG:3857 PNG tiles cut from 2 × 2 metatiles read and drawn with a
/// symbol buffer, styled by the render query (<see cref="RenderStyle"/> / <c>LayerResolver</c>), layers bottom first,
/// hidden layers skipped, the budget spent by layer then id, empty tiles <see cref="TilePayload.Empty"/>, cancellation
/// thrown rather than answered with an empty tile.
/// </summary>
public sealed class SkiaRasterTileSourceTests
{
    private const int Zoom = 12;

    /// <summary>The most a channel may differ between two drawings of the same pixels from frames of other origins (float rounding of anti-aliased edges).</summary>
    private const int PixelTolerance = 2;

    /// <summary>
    /// The most a channel of a long anti-aliased stroke may differ between drawings of other frames: Skia rasterizes a path
    /// clipped to its canvas, so the stroke's edge coverage can move by a few levels — never a gap or a shift.
    /// </summary>
    private const int LineTolerance = 32;

    private static readonly WKTReader Wkt = new();
    private static readonly TileAddress Anchor = TileAddress.Containing(-38.5, -13.0, Zoom);

    [Fact]
    public async Task Tile_IsA256PxPngOfItsMercatorEnvelope_WithAFeatureAtItsProjectedPixel()
    {
        var query = new FeatureListRenderQuery();
        query.Features.Add(Point("pole", "a", "#ff0000", Anchor, 100.5, 80.5));
        var source = new SkiaRasterTileSource(query, lodPolicy: null);

        var tile = await source.GetRasterTileAsync(Request(Anchor, "a"), CancellationToken.None);

        tile.ContentType.Should().Be(TileFormats.PngContentType);
        tile.IsEmpty.Should().BeFalse();
        using var bitmap = Decode(tile);
        bitmap.Width.Should().Be(SkiaRasterTileSource.TileSize);
        bitmap.Height.Should().Be(SkiaRasterTileSource.TileSize);
        var painted = bitmap.GetPixel(100, 80);
        painted.Alpha.Should().BeGreaterThan(150);
        painted.Red.Should().BeGreaterThan(200);
        painted.Green.Should().BeLessThan(60);
        painted.Blue.Should().BeLessThan(60);
        bitmap.GetPixel(200, 200).Alpha.Should().Be(0, "the tile is transparent away from its features");
    }

    [Fact]
    public async Task EveryFeature_IsStyledThroughLayerResolver()
    {
        var catalog = new TestAssetProvider();
        var type = new AssetType { Name = "Poste", Color = "#00ff00" };
        var flagged = new Layer { Name = "Flagged", Color = "#ff0000" };
        catalog.AddAssetType(type);
        catalog.AddLayer(flagged);
        catalog.AddLayerRule(new LayerRule
        {
            AssetTypeId = type.Id,
            LayerId = flagged.Id,
            Conditions = [new LayerRuleCondition { Attribute = "status", Operator = LayerRuleOperator.Equals, Value = "bad" }],
        });
        catalog.Add(Feature("plain", type.Id, Anchor, 60.5, 60.5, []));
        catalog.Add(Feature("bad", type.Id, Anchor, 180.5, 180.5, new(StringComparer.Ordinal) { ["status"] = "bad" }));
        var source = new SkiaRasterTileSource(new AssetProviderRenderQuery(catalog), lodPolicy: null);

        var tile = await source.GetRasterTileAsync(Request(Anchor, type.Id.ToString()), CancellationToken.None);

        using var bitmap = Decode(tile);
        var plain = bitmap.GetPixel(60, 60);
        plain.Green.Should().BeGreaterThan(200, "no rule matches: the asset type's color");
        plain.Red.Should().BeLessThan(60);
        var bad = bitmap.GetPixel(180, 180);
        bad.Red.Should().BeGreaterThan(200, "the matching layer rule's layer color");
        bad.Green.Should().BeLessThan(60);
    }

    [Fact]
    public async Task AnAssetTypeHiddenAtTheTilesZoom_IsNotDrawn()
    {
        var catalog = new TestAssetProvider();
        var type = new AssetType { Name = "Detail", Color = "#ff0000", MinZoom = Zoom + 1 };
        catalog.AddAssetType(type);
        catalog.Add(Feature("detail", type.Id, Anchor, 100.5, 100.5, []));
        var source = new SkiaRasterTileSource(new AssetProviderRenderQuery(catalog), lodPolicy: null);

        var hidden = await source.GetRasterTileAsync(Request(Anchor, type.Id.ToString()), CancellationToken.None);
        var shown = await source.GetRasterTileAsync(Request(Descendant(Anchor), type.Id.ToString()), CancellationToken.None);

        hidden.IsEmpty.Should().BeTrue();
        shown.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task Layers_AreDrawnBottomFirst_OneReadEach_AtTheTilesZoom_OverTheBufferedMetatile()
    {
        var query = new FeatureListRenderQuery();
        query.Features.Add(Point("blue", "below", "#0000ff", Anchor, 128.5, 128.5));
        query.Features.Add(Point("red", "above", "#ff0000", Anchor, 128.5, 128.5));
        var source = new SkiaRasterTileSource(query, lodPolicy: null);

        var tile = await source.GetRasterTileAsync(Request(Anchor, "below", "above"), CancellationToken.None);

        using var bitmap = Decode(tile);
        var top = bitmap.GetPixel(128, 128);
        top.Red.Should().BeGreaterThan(150, "the last layer is drawn on top");
        top.Blue.Should().BeLessThan(100);
        query.Renders.Select(r => r.Options.AssetTypeId).Should().Equal("below", "above");
        query.Renders.Should().OnlyContain(r => r.Options.Zoom == Zoom);
        query.Renders.Select(r => r.Bbox).Distinct().Should().ContainSingle().Which.Should().Be(BufferedMetatileBox(Anchor));
    }

    [Fact]
    public async Task ReadBox_IsTheMetatileGrownBySymbolBufferPixels()
    {
        var query = new FeatureListRenderQuery();
        var source = new SkiaRasterTileSource(query, lodPolicy: null);

        await source.GetRasterTileAsync(Request(Anchor, "a"), CancellationToken.None);

        var box = query.Renders.Should().ContainSingle().Subject.Bbox;
        var range = Anchor.Metatile(2);
        var northWest = new TileAddress(Zoom, range.MinX, range.MinY).Envelope4326;
        var southEast = new TileAddress(Zoom, range.MaxX, range.MaxY).Envelope4326;
        var degreesPerPixel = (northWest.MaxLon - northWest.MinLon) / SkiaRasterTileSource.TileSize;
        box.MinLon.Should().BeApproximately(northWest.MinLon - (SkiaRasterTileSource.SymbolBuffer * degreesPerPixel), 1e-9);
        box.MaxLon.Should().BeApproximately(southEast.MaxLon + (SkiaRasterTileSource.SymbolBuffer * degreesPerPixel), 1e-9);
        box.MaxLat.Should().BeGreaterThan(northWest.MaxLat);
        box.MinLat.Should().BeLessThan(southEast.MinLat);
        SkiaRasterTileSource.SymbolBuffer.Should().BeGreaterThan((int)Math.Ceiling(SkiaFeaturePainter.MaxSymbolOverhang));
    }

    [Fact]
    public async Task ALayerTheLodPolicyHides_IsNotRead_AndAllHidden_IsEmptyWithoutARead()
    {
        var query = new FeatureListRenderQuery();
        query.Features.Add(Point("p", "hidden", "#ff0000", Anchor, 100.5, 100.5));
        var policy = new LodPolicy(LodThresholds.VectorOnly, new Dictionary<string, LodThresholds>(StringComparer.Ordinal) { ["hidden"] = new(0, 0, MinZoom: Zoom + 1) });
        var source = new SkiaRasterTileSource(query, policy);

        var allHidden = await source.GetRasterTileAsync(Request(Anchor, "hidden"), CancellationToken.None);

        allHidden.IsEmpty.Should().BeTrue();
        allHidden.ContentType.Should().Be(TileFormats.PngContentType);
        query.Renders.Should().BeEmpty();

        await source.GetRasterTileAsync(Request(Anchor, "hidden", "shown"), CancellationToken.None);

        query.Renders.Select(r => r.Options.AssetTypeId).Should().Equal("shown");
    }

    [Fact]
    public async Task Budget_IsSpentByLayerThenId_LaterLayersGetWhatIsLeft()
    {
        var query = new FeatureListRenderQuery();
        query.Features.Add(Point("a2", "a", "#ff0000", Anchor, 10.5, 10.5));
        query.Features.Add(Point("a1", "a", "#ff0000", Anchor, 30.5, 30.5));
        foreach (var i in Enumerable.Range(0, 5))
            query.Features.Add(Point($"b{i}", "b", "#00ff00", Anchor, 50.5 + (i * 20), 50.5));
        query.Features.Add(Point("c0", "c", "#0000ff", Anchor, 200.5, 200.5));
        var source = new SkiaRasterTileSource(query, lodPolicy: null, maxFeatures: 3);

        var tile = await source.GetRasterTileAsync(Request(Anchor, "a", "b", "c"), CancellationToken.None);

        query.Renders.Select(r => (r.Options.AssetTypeId, r.Options.MaxCount)).Should().Equal(("a", 3), ("b", 1));
        using var bitmap = Decode(tile);
        bitmap.GetPixel(50, 50).Alpha.Should().BeGreaterThan(0, "b0 is the first of b by id");
        bitmap.GetPixel(70, 50).Alpha.Should().Be(0, "the budget ran out after b0");
        bitmap.GetPixel(200, 200).Alpha.Should().Be(0, "c is never read");
    }

    [Fact]
    public async Task NothingToDraw_IsTheEmptyPngPayload()
    {
        var source = new SkiaRasterTileSource(new FeatureListRenderQuery(), lodPolicy: null);

        var tile = await source.GetRasterTileAsync(Request(Anchor, "a"), CancellationToken.None);

        tile.IsEmpty.Should().BeTrue();
        tile.ContentType.Should().Be(TileFormats.PngContentType);
        tile.ContentEncoding.Should().BeNull();
    }

    [Fact]
    public async Task ATileOfTheMetatileWithoutInk_IsEmpty_WhileTheOneWithItIsNot()
    {
        var query = new FeatureListRenderQuery();
        var range = Anchor.Metatile(2);
        var inked = new TileAddress(Zoom, range.MinX, range.MinY);
        query.Features.Add(Point("p", "a", "#ff0000", inked, 128.5, 128.5));
        var source = new SkiaRasterTileSource(query, lodPolicy: null);

        var tiles = await source.GetMetatileAsync(Request(Anchor, "a"), CancellationToken.None);

        tiles.Should().HaveCount(4);
        tiles[inked].IsEmpty.Should().BeFalse();
        tiles.Where(t => t.Key != inked).Should().OnlyContain(t => t.Value.IsEmpty);
    }

    [Fact]
    public async Task PointsOnAndNearTheEdgeBetweenTwoMetatiles_AreDrawnWholeOnBothSides_WithoutASeam()
    {
        var left = new TileAddress(Zoom, (Anchor.X / 4 * 4) + 1, Anchor.Y);
        var right = new TileAddress(Zoom, left.X + 1, Anchor.Y);
        left.Metatile(2).Should().NotBe(right.Metatile(2), "the edge between them is a metatile edge");
        var query = new FeatureListRenderQuery();
        query.Features.Add(Point("edge", "a", "#ff0000", right, 0, 128));
        query.Features.Add(Point("just-east", "a", "#00aa00", right, 3, 60));
        query.Features.Add(Point("just-west", "a", "#0000ff", left, 253, 200));
        var twoByTwo = new SkiaRasterTileSource(query, lodPolicy: null);
        var fourByFour = new SkiaRasterTileSource(query, lodPolicy: null, metatileSpan: 4);

        var leftTile = await twoByTwo.GetRasterTileAsync(Request(left, "a"), CancellationToken.None);
        var rightTile = await twoByTwo.GetRasterTileAsync(Request(right, "a"), CancellationToken.None);

        using (var leftBitmap = Decode(leftTile))
        using (var rightBitmap = Decode(rightTile))
        {
            leftBitmap.GetPixel(SkiaRasterTileSource.TileSize - 1, 128).Alpha.Should().BeGreaterThan(150);
            leftBitmap.GetPixel(SkiaRasterTileSource.TileSize - 5, 128).Alpha.Should().BeGreaterThan(0, "the circle's west half is in the west tile");
            rightBitmap.GetPixel(0, 128).Alpha.Should().BeGreaterThan(150);
            rightBitmap.GetPixel(4, 128).Alpha.Should().BeGreaterThan(0, "the circle's east half is in the east tile");
            leftBitmap.GetPixel(SkiaRasterTileSource.TileSize - 1, 60).Alpha.Should().BeGreaterThan(0, "a symbol just past the metatile's edge is read through the buffer and drawn");
            rightBitmap.GetPixel(0, 200).Alpha.Should().BeGreaterThan(0, "a symbol just past the metatile's edge is read through the buffer and drawn");
        }

        MaxDifference(leftTile, await fourByFour.GetRasterTileAsync(Request(left, "a"), CancellationToken.None))
            .Should().BeLessThanOrEqualTo(PixelTolerance, "each side matches a drawing where both tiles are one image");
        MaxDifference(rightTile, await fourByFour.GetRasterTileAsync(Request(right, "a"), CancellationToken.None))
            .Should().BeLessThanOrEqualTo(PixelTolerance);
    }

    [Fact]
    public async Task MetatileSlices_OfSymbolsAndAreas_HaveThePixelsOfEachTileDrawnAlone()
    {
        var query = new FeatureListRenderQuery();
        var range = Anchor.Metatile(2);
        var northWest = new TileAddress(Zoom, range.MinX, range.MinY);
        var southEast = new TileAddress(Zoom, range.MaxX, range.MaxY);
        query.Features.Add(Point("inner-corner", "a", "#ff0000", southEast, 0, 0));
        query.Features.Add(Point("inner-edge", "a", "#00aa00", northWest, 256, 40.25));
        query.Features.Add(Polygon("square", "a", "#e9c46a", northWest, 200.2, 200.6, southEast, 60.4, 50.8));
        var metatiled = new SkiaRasterTileSource(query, lodPolicy: null);
        var alone = new SkiaRasterTileSource(query, lodPolicy: null, metatileSpan: 1);

        var slices = await metatiled.GetMetatileAsync(Request(Anchor, "a"), CancellationToken.None);

        slices.Keys.ToHashSet().SetEquals(range.EnumerateTiles()).Should().BeTrue("every tile of the metatile, and only those");
        slices.Values.Should().OnlyContain(t => !t.IsEmpty);
        foreach (var (address, slice) in slices)
        {
            var direct = await alone.GetRasterTileAsync(Request(address, "a"), CancellationToken.None);
            MaxDifference(slice, direct).Should().BeLessThanOrEqualTo(PixelTolerance, $"tile {address} cut from the metatile matches it drawn alone");
        }
    }

    [Fact]
    public async Task ALineAcrossTheEdgeBetweenTwoMetatiles_ContinuesAcrossIt()
    {
        var left = new TileAddress(Zoom, (Anchor.X / 4 * 4) + 1, Anchor.Y);
        var right = new TileAddress(Zoom, left.X + 1, Anchor.Y);
        var query = new FeatureListRenderQuery();
        query.Features.Add(Line("slanted", "a", "#ff0000", left, (128.3, 100.7), right, (128.9, 156.1)));
        var twoByTwo = new SkiaRasterTileSource(query, lodPolicy: null);
        var fourByFour = new SkiaRasterTileSource(query, lodPolicy: null, metatileSpan: 4);

        var leftTile = await twoByTwo.GetRasterTileAsync(Request(left, "a"), CancellationToken.None);
        var rightTile = await twoByTwo.GetRasterTileAsync(Request(right, "a"), CancellationToken.None);

        using (var leftBitmap = Decode(leftTile))
        using (var rightBitmap = Decode(rightTile))
        {
            Enumerable.Range(120, 16).Max(y => leftBitmap.GetPixel(SkiaRasterTileSource.TileSize - 1, y).Alpha).Should().BeGreaterThan(150);
            Enumerable.Range(120, 16).Max(y => rightBitmap.GetPixel(0, y).Alpha).Should().BeGreaterThan(150);
        }

        MaxDifference(leftTile, await fourByFour.GetRasterTileAsync(Request(left, "a"), CancellationToken.None))
            .Should().BeLessThanOrEqualTo(LineTolerance, "each side matches a drawing where both tiles are one image, up to the anti-aliasing of a clipped stroke");
        MaxDifference(rightTile, await fourByFour.GetRasterTileAsync(Request(right, "a"), CancellationToken.None))
            .Should().BeLessThanOrEqualTo(LineTolerance);
    }

    [Fact]
    public async Task GetMetatile_IsOneReadPerLayer_AndEachTileIsWhatGetRasterTileAnswersForIt()
    {
        var query = new FeatureListRenderQuery();
        query.Features.Add(Line("line", "a", "#264653", Anchor, (-300, -300), Anchor, (500, 500)));
        var source = new SkiaRasterTileSource(query, lodPolicy: null);

        var slices = await source.GetMetatileAsync(Request(Anchor, "a"), CancellationToken.None);

        query.Renders.Should().ContainSingle();
        foreach (var (address, slice) in slices)
        {
            var single = await source.GetRasterTileAsync(Request(address, "a"), CancellationToken.None);
            single.Content.ToArray().Should().Equal(slice.Content.ToArray());
            single.ETag.Should().Be(slice.ETag);
        }
    }

    [Fact]
    public async Task AtZoomZero_TheMetatileIsTheWorldTileAlone()
    {
        var query = new FeatureListRenderQuery();
        query.Features.Add(new RenderableFeature("p", "p", "a", new GeoPoint(-38.5, -13.0), "#ff0000"));
        var source = new SkiaRasterTileSource(query, lodPolicy: null);
        var world = new TileAddress(0, 0, 0);

        var slices = await source.GetMetatileAsync(Request(world, "a"), CancellationToken.None);

        slices.Keys.Should().Equal(world);
        slices[world].IsEmpty.Should().BeFalse();
        using var bitmap = Decode(slices[world]);
        bitmap.Width.Should().Be(SkiaRasterTileSource.TileSize);
    }

    [Fact]
    public async Task AnAlreadyCancelledRequest_ThrowsBeforeAnyRead()
    {
        var query = new FeatureListRenderQuery();
        var source = new SkiaRasterTileSource(query, lodPolicy: null);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var tile = () => source.GetRasterTileAsync(Request(Anchor, "a"), cancelled.Token);
        var metatile = () => source.GetMetatileAsync(Request(Anchor, "a"), cancelled.Token);

        await tile.Should().ThrowAsync<OperationCanceledException>();
        await metatile.Should().ThrowAsync<OperationCanceledException>();
        query.Renders.Should().BeEmpty();
    }

    [Fact]
    public async Task ARequestCancelledDuringARead_ThrowsRatherThanAnsweringAnEmptyTile()
    {
        using var cancellation = new CancellationTokenSource();
        var query = new FeatureListRenderQuery { CancelDuringRead = cancellation };
        var source = new SkiaRasterTileSource(query, lodPolicy: null);

        var tile = () => source.GetRasterTileAsync(Request(Anchor, "a", "b"), cancellation.Token);

        await tile.Should().ThrowAsync<OperationCanceledException>();
        query.Renders.Should().ContainSingle("no further layer is read once cancelled");
    }

    [Fact]
    public void Constructor_RefusesAMissingQuery_AndANonPositiveBudgetOrSpan()
    {
        var query = new FeatureListRenderQuery();

        ((Action)(() => _ = new SkiaRasterTileSource(null!, null))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new SkiaRasterTileSource(query, null, maxFeatures: 0))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => _ = new SkiaRasterTileSource(query, null, metatileSpan: 0))).Should().Throw<ArgumentOutOfRangeException>();
        var source = new SkiaRasterTileSource(query, null);
        source.MaxFeatures.Should().Be(RenderQueryOptions.DefaultMaxCount);
        source.MetatileSpan.Should().Be(2);
    }

    [Fact]
    public async Task ANullRequest_IsRefused()
    {
        var source = new SkiaRasterTileSource(new FeatureListRenderQuery(), null);

        await ((Func<Task>)(() => source.GetRasterTileAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => source.GetMetatileAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task AddSkiaRasterTileSource_RegistersAScopedSourceOverTheRenderQueryAndLodPolicy()
    {
        var query = new FeatureListRenderQuery();
        var services = new ServiceCollection();
        services.AddScoped<IAssetRenderQuery>(_ => query);
        services.AddSingleton<ILodPolicy>(new LodPolicy(LodThresholds.VectorOnly, new Dictionary<string, LodThresholds>(StringComparer.Ordinal) { ["hidden"] = new(0, 0, MaxZoom: 1) }));

        services.AddSkiaRasterTileSource();

        services.Should().ContainSingle(d => d.ServiceType == typeof(IRasterTileSource)).Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var source = scope.ServiceProvider.GetRequiredService<IRasterTileSource>().Should().BeOfType<SkiaRasterTileSource>().Subject;
        await source.GetRasterTileAsync(Request(Anchor, "hidden", "shown"), CancellationToken.None);
        query.Renders.Select(r => r.Options.AssetTypeId).Should().Equal("shown");
    }

    [Fact]
    public void AddSkiaRasterTileSource_KeepsARasterSourceAlreadyRegistered()
    {
        var existing = new SkiaRasterTileSource(new FeatureListRenderQuery(), null);
        var services = new ServiceCollection();
        services.AddSingleton<IRasterTileSource>(existing);

        services.AddSkiaRasterTileSource();

        services.Should().ContainSingle(d => d.ServiceType == typeof(IRasterTileSource)).Which.ImplementationInstance.Should().BeSameAs(existing);
    }

    private static TileRequest Request(TileAddress address, params string[] layers) => new(new TileLayerSet("set", layers), address);

    private static TileAddress Descendant(TileAddress tile) => tile.Descendants(tile.Z + 1).EnumerateTiles().First();

    /// <summary>The longitude/latitude of pixel (<paramref name="px"/>, <paramref name="py"/>) of <paramref name="tile"/>.</summary>
    private static (double Lon, double Lat) LonLatAt(TileAddress tile, double px, double py)
    {
        var envelope = tile.Envelope3857;
        return WebMercator.Unproject(
            envelope.MinX + (px / SkiaRasterTileSource.TileSize * envelope.Width),
            envelope.MaxY - (py / SkiaRasterTileSource.TileSize * envelope.Height));
    }

    private static BoundingBox BufferedMetatileBox(TileAddress tile)
    {
        var range = tile.Metatile(2);
        var northWest = new TileAddress(range.Zoom, range.MinX, range.MinY).Envelope3857;
        var southEast = new TileAddress(range.Zoom, range.MaxX, range.MaxY).Envelope3857;
        var buffer = SkiaRasterTileSource.SymbolBuffer * northWest.Width / SkiaRasterTileSource.TileSize;
        return WebMercator.Unproject(new MercatorBounds(northWest.MinX - buffer, southEast.MinY - buffer, southEast.MaxX + buffer, northWest.MaxY + buffer));
    }

    private static RenderableFeature Point(string id, string layer, string color, TileAddress tile, double px, double py)
    {
        var (lon, lat) = LonLatAt(tile, px, py);
        return new RenderableFeature(id, id, layer, new GeoPoint(lon, lat), color);
    }

    private static RenderableFeature Line(
        string id, string layer, string color, TileAddress fromTile, (double X, double Y) from, TileAddress toTile, (double X, double Y) to)
    {
        var a = LonLatAt(fromTile, from.X, from.Y);
        var b = LonLatAt(toTile, to.X, to.Y);
        return new RenderableFeature(id, id, layer, new GeoLineString([a, b]), color);
    }

    private static RenderableFeature Polygon(
        string id, string layer, string color, TileAddress westTile, double westX, double northY, TileAddress eastTile, double eastX, double southY)
    {
        var (west, north) = LonLatAt(westTile, westX, northY);
        var (east, south) = LonLatAt(eastTile, eastX, southY);
        var wkt = FormattableString.Invariant($"POLYGON (({west} {south}, {east} {south}, {east} {north}, {west} {north}, {west} {south}))");
        return new RenderableFeature(id, id, layer, GeoGeometry.FromNts(Wkt.Read(wkt)), color);
    }

    private static GeoFeature Feature(string id, Guid assetTypeId, TileAddress tile, double px, double py, Dictionary<string, string> attributes)
    {
        var (lon, lat) = LonLatAt(tile, px, py);
        return new GeoFeature
        {
            Id = id,
            Geometry = new GeoPoint(lon, lat),
            Properties = new GeoFeatureProperties { Name = id, AssetTypeId = assetTypeId.ToString(), CustomAttributes = attributes },
        };
    }

    private static SKBitmap Decode(TilePayload tile) => SKBitmap.Decode(tile.Content.ToArray());

    private static int MaxDifference(TilePayload a, TilePayload b)
    {
        if (a.IsEmpty || b.IsEmpty) return a.IsEmpty == b.IsEmpty ? 0 : byte.MaxValue;

        using var first = Decode(a);
        using var second = Decode(b);
        var x = first.GetPixelSpan();
        var y = second.GetPixelSpan();
        x.Length.Should().Be(y.Length);
        var max = 0;
        for (var i = 0; i < x.Length; i++)
            max = Math.Max(max, Math.Abs(x[i] - y[i]));
        return max;
    }

    /// <summary>An in-memory render query: the features of the asked layer whose envelope meets the box, by ordinal id, capped.</summary>
    private sealed class FeatureListRenderQuery : IAssetRenderQuery
    {
        public List<RenderableFeature> Features { get; } = [];

        public List<(BoundingBox Bbox, RenderQueryOptions Options)> Renders { get; } = [];

        /// <summary>Cancelled by the read itself, which then answers no features — as <c>PostgresAssetRenderQuery</c> answers a cancelled read.</summary>
        public CancellationTokenSource? CancelDuringRead { get; init; }

        public async Task<IReadOnlyList<RenderableFeature>> GetRenderableAsync(BoundingBox bbox, RenderQueryOptions options, CancellationToken ct)
        {
            Renders.Add((bbox, options));
            if (CancelDuringRead is { } cancellation)
            {
                await cancellation.CancelAsync();
                return [];
            }

            var box = new NetTopologySuite.Geometries.Envelope(bbox.MinLon, bbox.MaxLon, bbox.MinLat, bbox.MaxLat);
            return
            [
                .. Features
                    .Where(f => options.AssetTypeId is null || string.Equals(f.AssetTypeId, options.AssetTypeId, StringComparison.Ordinal))
                    .Where(f => box.Intersects(f.Geometry.NtsGeometry.EnvelopeInternal))
                    .OrderBy(f => f.Id, StringComparer.Ordinal)
                    .Take(options.MaxCount),
            ];
        }

        public Task<IReadOnlyList<FeatureInfo>> GetFeatureInfoAsync(BoundingBox bbox, int maxCount, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FeatureInfo>>([]);
    }
}
