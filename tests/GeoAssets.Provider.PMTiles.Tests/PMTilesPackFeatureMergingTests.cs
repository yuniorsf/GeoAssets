using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PMTiles.Packs;
using NetTopologySuite.Geometries;
using Xunit;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>
/// XD01-463 — <see cref="PMTilesPackAssetProvider"/> joins the pieces a pack's tiles cut a feature into: a polygon
/// spanning tiles is one feature with its own outline (no seam edges, no buffer slivers), a line crossing a seam one
/// line, a point feature once; pieces without an id, or with an id a tile repeats, stay one feature per tile.
/// </summary>
public sealed class PMTilesPackFeatureMergingTests : IDisposable
{
    private const int Zoom = 10;
    private const int X0 = 500;
    private const int Y0 = 400;

    /// <summary>The corner the four tiles X0…X0+1, Y0…Y0+1 share, in world pixels.</summary>
    private const int CornerX = (X0 + 1) * TestTiler.Extent;
    private const int CornerY = (Y0 + 1) * TestTiler.Extent;

    /// <summary>A grid unit of the read, in degrees of longitude: one tile pixel at <see cref="Zoom"/>.</summary>
    private const double Pixel = 360.0 / (1 << Zoom) / TestTiler.Extent;

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    /// <summary>An irregular octagon around the corner the four tiles share, with a hole that crosses the seams too.</summary>
    private static Polygon Octagon()
    {
        var shell = new[] { (-1500, -300), (-900, -1400), (200, -1700), (1300, -1100), (1700, 100), (1100, 1300), (-100, 1600), (-1300, 1000) };
        var hole = new[] { (-300, -300), (300, -300), (300, 300), (-300, 300) };
        var factory = new GeometryFactory();
        LinearRing Ring((int X, int Y)[] vertices) =>
            factory.CreateLinearRing([.. vertices.Select(v => new Coordinate(CornerX + v.X, CornerY + v.Y)), new Coordinate(CornerX + vertices[0].X, CornerY + vertices[0].Y)]);
        return factory.CreatePolygon(Ring(shell), [Ring(hole)]);
    }

    private async Task<PMTilesPackAssetProvider> ProviderAsync(IEnumerable<(TileAddress Tile, TestMvt.Feature Piece)> pieces)
    {
        var pack = await PMTilesPack.OpenAsync(TestTiler.Archive(pieces, Zoom).WriteTo(_directory.Path), CancellationToken.None);
        return new PMTilesPackAssetProvider(pack);
    }

    private static Task<IReadOnlyList<GeoFeature>> ReadFourTilesAsync(PMTilesPackAssetProvider provider) =>
        provider.ReadAsync(TestTiler.Inside(Zoom, X0, Y0, X0 + 1, Y0 + 1), Zoom + 1);

    [Fact]
    public async Task APolygonSpanningTwoByTwoTiles_IsOneFeature_WhoseOutlineHasNoSeamEdges()
    {
        var octagon = Octagon();
        var pieces = TestTiler.Cut(Zoom, octagon, id: 42).ToList();
        pieces.Select(p => p.Tile).Should().HaveCount(4, "the tiler cut it into one piece per tile");
        using var provider = await ProviderAsync(pieces);

        var features = await ReadFourTilesAsync(provider);

        var feature = features.Should().ContainSingle().Subject;
        feature.Id.Should().Be($"pmtiles:{provider.Pack.Id}:{Zoom}:polygon:42:shapes");
        var polygon = feature.Geometry!.NtsGeometry.Should().BeOfType<Polygon>().Subject;
        polygon.NumInteriorRings.Should().Be(1, "the hole the seams cut through is whole again");

        var original = TestTiler.ToLonLat(Zoom, octagon);
        var outline = original.Boundary;
        foreach (var ring in new[] { polygon.ExteriorRing }.Concat(polygon.InteriorRings))
        {
            var vertices = ring.Coordinates;
            for (var i = 0; i + 1 < vertices.Length; i++)
            {
                var midpoint = new Coordinate((vertices[i].X + vertices[i + 1].X) / 2, (vertices[i].Y + vertices[i + 1].Y) / 2);
                outline.Distance(new Point(vertices[i])).Should().BeLessThan(2 * Pixel, "every vertex is on the polygon's own outline");
                outline.Distance(new Point(midpoint)).Should().BeLessThan(2 * Pixel, "no edge runs along a seam or a buffer edge");
            }
        }

        polygon.Area.Should().BeApproximately(original.Area, original.Area * 1e-3, "the buffer overlap is dissolved, not left as slivers or gaps");
        polygon.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ALineCrossingASeam_IsOneLine()
    {
        var line = TestTiler.Line((CornerX - 1800, CornerY - 700), (CornerX - 300, CornerY - 500), (CornerX + 900, CornerY - 900), (CornerX + 1900, CornerY - 200));
        var pieces = TestTiler.Cut(Zoom, line, id: 7).ToList();
        pieces.Should().HaveCount(2);
        using var provider = await ProviderAsync(pieces);

        var features = await ReadFourTilesAsync(provider);

        var feature = features.Should().ContainSingle().Subject;
        feature.Id.Should().Be($"pmtiles:{provider.Pack.Id}:{Zoom}:line:7:shapes");
        var joined = feature.Geometry!.NtsGeometry.Should().BeOfType<LineString>().Subject;
        var original = TestTiler.ToLonLat(Zoom, line);
        joined.Length.Should().BeApproximately(original.Length, 4 * Pixel, "neither the buffer overlap nor a gap is left at the seam");
        var ends = new[] { joined.StartPoint, joined.EndPoint };
        foreach (var end in new[] { original.Coordinates[0], original.Coordinates[^1] })
            ends.Min(e => e.Distance(new Point(end))).Should().BeLessThan(Pixel, "the line ends where it did");
        foreach (var vertex in joined.Coordinates)
            original.Distance(new Point(vertex)).Should().BeLessThan(2 * Pixel);
    }

    [Fact]
    public async Task ASmallPolygonAcrossASeam_LyingWholeInTheNeighboursBuffer_IsThatPieceExactly()
    {
        var square = TestTiler.Polygon((CornerX - 30, CornerY - 500), (CornerX + 20, CornerY - 500), (CornerX + 20, CornerY - 450), (CornerX - 30, CornerY - 450));
        using var provider = await ProviderAsync(TestTiler.Cut(Zoom, square, id: 9));

        var features = await ReadFourTilesAsync(provider);

        var polygon = features.Should().ContainSingle().Subject.Geometry!.NtsGeometry.Should().BeOfType<Polygon>().Subject;
        var original = TestTiler.ToLonLat(Zoom, square);
        polygon.ExteriorRing.Coordinates.Should().HaveCount(5);
        polygon.Normalized().EqualsExact(original.Normalized(), 1e-6).Should().BeTrue("the piece in either tile is the whole square, no seam cut through it");
    }

    [Fact]
    public async Task ALineCrossingSeveralTiles_ThereAndBack_IsOneFeature()
    {
        var line = TestTiler.Line((CornerX - 1800, CornerY - 1800), (CornerX + 1800, CornerY - 1700), (CornerX + 1700, CornerY + 1800), (CornerX - 1700, CornerY + 1700));
        using var provider = await ProviderAsync(TestTiler.Cut(Zoom, line, id: 8));

        var features = await ReadFourTilesAsync(provider);

        var joined = features.Should().ContainSingle().Subject.Geometry!.NtsGeometry.Should().BeOfType<LineString>().Subject;
        joined.Length.Should().BeApproximately(TestTiler.ToLonLat(Zoom, line).Length, 8 * Pixel);
    }

    [Fact]
    public async Task APointFeatureSpanningTiles_IsOneFeature_EachPointOnce()
    {
        var points = TestTiler.Points((CornerX - 30, CornerY - 30), (CornerX + 30, CornerY + 30));
        using var provider = await ProviderAsync(TestTiler.Cut(Zoom, points, id: 3));

        var features = await ReadFourTilesAsync(provider);

        var feature = features.Should().ContainSingle().Subject;
        feature.Id.Should().Be($"pmtiles:{provider.Pack.Id}:{Zoom}:point:3:shapes");
        feature.Geometry!.NtsGeometry.Should().BeOfType<MultiPoint>().Which.NumGeometries.Should().Be(2);
    }

    [Fact]
    public async Task APolygonWithoutAnId_StaysOnePiecePerTile_AsBefore()
    {
        using var provider = await ProviderAsync(TestTiler.Cut(Zoom, Octagon(), id: null));

        var features = await ReadFourTilesAsync(provider);

        features.Should().HaveCount(4);
        features.Select(f => f.Id).Should().BeEquivalentTo(
            Enumerable.Range(0, 4).Select(i => $"pmtiles:{provider.Pack.Id}:{new TileAddress(Zoom, X0 + (i % 2), Y0 + (i / 2))}:0:0"));
        features.Should().OnlyContain(f => f.Geometry!.Type == "Polygon");
    }

    [Fact]
    public async Task AnIdATileRepeats_IsNoIdentity_SoItsPiecesStayPerTile()
    {
        var octagon = Octagon();
        var other = TestTiler.Polygon((CornerX - 1000, CornerY - 1000), (CornerX - 900, CornerY - 1000), (CornerX - 900, CornerY - 900));
        using var provider = await ProviderAsync([.. TestTiler.Cut(Zoom, octagon, id: 0), .. TestTiler.Cut(Zoom, other, id: 0)]);

        var features = await ReadFourTilesAsync(provider);

        features.Should().HaveCount(5, "four pieces of the octagon and the small triangle, each per tile");
        features.Should().OnlyContain(f => f.Id.Contains('/', StringComparison.Ordinal));
    }

    [Fact]
    public async Task FeaturesWithAndWithoutIds_AreEachAnsweredOnce_InTheOrderFirstSeen()
    {
        var keyed = TestTiler.Cut(Zoom, Octagon(), id: 1);
        var unkeyed = TestTiler.Cut(Zoom, TestTiler.Points((CornerX - 100, CornerY - 100)), id: null);
        using var provider = await ProviderAsync([.. keyed, .. unkeyed]);

        var features = await ReadFourTilesAsync(provider);

        features.Select(f => f.Geometry!.Type).Should().Equal("Polygon", "Point");
        provider.GetById(features[0].Id).Should().BeSameAs(features[0]);
    }

    [Fact]
    public async Task TheFeatureBound_CountsPieces_SoAReadStillAnswersAtMostTheFeatureLimit()
    {
        const int perTile = 6_000;
        var squares = Enumerable.Range(0, perTile).Select(i =>
        {
            var y = (Y0 * TestTiler.Extent) + 100 + (i % 3000);
            return (Id: (ulong)i, Shape: TestTiler.Polygon((CornerX - 20, y), (CornerX + 100, y), (CornerX + 100, y + 1), (CornerX - 20, y + 1)));
        });
        var pieces = squares.SelectMany(s => TestTiler.Cut(Zoom, s.Shape, s.Id)).ToList();
        pieces.Should().HaveCount(2 * perTile, "each square straddles the seam");
        using var provider = await ProviderAsync(pieces);

        var features = await provider.ReadAsync(TestTiler.Inside(Zoom, X0, Y0, X0 + 1, Y0), Zoom + 1);

        features.Should().HaveCount(perTile, "the second tile's first 4,000 pieces join their squares, the rest are not decoded");
        features.Count.Should().BeLessThanOrEqualTo(PMTilesPackAssetProvider.MaxFeaturesPerRead);
        features.Count(f => f.Geometry!.NtsGeometry.EnvelopeInternal.MaxX > TestTiler.ToLonLat(Zoom, CornerX + 80, 0).X)
            .Should().Be(PMTilesPackAssetProvider.MaxFeaturesPerRead - perTile, "only the joined squares reach past the first tile's buffer");
    }

    [Fact]
    public async Task TheReadRunsOffTheCallersThread()
    {
        var cache = new ContextRecordingTileCache();
        using var pack = await PMTilesPack.OpenAsync(TestTiler.Archive(TestTiler.Cut(Zoom, Octagon(), id: 1), Zoom).WriteTo(_directory.Path), CancellationToken.None);
        using var provider = new PMTilesPackAssetProvider(pack, cache);
        var caller = new SynchronizationContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(caller);
        try
        {
            await ReadFourTilesAsync(provider);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        cache.Contexts.Should().NotBeEmpty().And.OnlyContain(c => c == null, "every tile is read and decoded on the thread pool");
    }

    [Fact]
    public void ALineCut_KeepsOnlyThePartsInsideTheBox_EndingExactlyOnIt()
    {
        var box = new Envelope(0, 1, 0, 1);
        Coordinate[] line = [new(-0.5, 0.5), new(0.5, 0.5), new(0.5, 1.5), new(0.75, 1.5), new(0.75, 0.5), new(1.25, 0.5)];

        var runs = PMTilesPackFeatureMerger.Cut(line, box);

        runs.Should().HaveCount(2);
        runs[0].Should().Equal(new Coordinate(0, 0.5), new Coordinate(0.5, 0.5), new Coordinate(0.5, 1));
        runs[1].Should().Equal(new Coordinate(0.75, 1), new Coordinate(0.75, 0.5), new Coordinate(1, 0.5));
        PMTilesPackFeatureMerger.Cut([new(2, 2), new(3, 3)], box).Should().BeEmpty();
        PMTilesPackFeatureMerger.Cut([new(0.5, 0.5), new(0.5, 0.5)], box).Should().BeEmpty("a point is no line");
    }

    private sealed class ContextRecordingTileCache : ITileCache
    {
        private readonly RecordingTileCache _inner = new();

        public List<SynchronizationContext?> Contexts { get; } = [];

        public Task<TilePayload?> GetAsync(TileCacheKey key, CancellationToken ct)
        {
            Contexts.Add(SynchronizationContext.Current);
            return _inner.GetAsync(key, ct);
        }

        public Task SetAsync(TileCacheKey key, TilePayload payload, CancellationToken ct) => _inner.SetAsync(key, payload, ct);

        public Task RemoveAsync(TileRange range, CancellationToken ct) => _inner.RemoveAsync(range, ct);
    }
}
