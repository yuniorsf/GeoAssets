using FluentAssertions;
using GeoAssets.Provider.PMTiles.Mvt;
using Xunit;

namespace GeoAssets.Provider.PMTiles.Tests;

/// <summary>XD01-438 — <see cref="MvtTileDecoder"/> against tiles <see cref="TestMvt"/> encodes from the MVT 2.1 spec.</summary>
public sealed class MvtTileDecoderTests
{
    [Fact]
    public void Layers_AreDecodedWithNameExtentAndFeatures_InTileOrder()
    {
        var tile = TestMvt.Encode(
            new TestMvt.Layer("roads", [TestMvt.Line([0, 0], [10, 20])], Extent: 512),
            new TestMvt.Layer("pois", [TestMvt.PointAt(5, 6), TestMvt.PointAt(7, 8)]));

        var layers = MvtTileDecoder.Decode(tile, maxFeatures: 100);

        layers.Select(l => (l.Name, l.Extent, l.Features.Count)).Should().Equal(("roads", 512, 1), ("pois", 4096, 2));
    }

    [Fact]
    public void ALayerWithoutAnExtent_HasTheDefault()
    {
        var tile = TestMvt.Encode(new TestMvt.Layer("pois", [TestMvt.PointAt(1, 1)], Extent: null));

        MvtTileDecoder.Decode(tile, 10).Single().Extent.Should().Be(MvtTileDecoder.DefaultExtent);
    }

    [Fact]
    public void PointGeometry_IsDecodedFromZigZagDeltas_OnePartPerPoint()
    {
        var tile = TestMvt.Encode(new TestMvt.Layer("pois", [new TestMvt.Feature(TestMvt.Point, [[[25, 17], [3, 4000]]])]));

        var feature = MvtTileDecoder.Decode(tile, 10).Single().Features.Single();

        feature.GeometryType.Should().Be(MvtGeometryType.Point);
        feature.Parts.Select(p => p.Single()).Should().Equal(new MvtPoint(25, 17), new MvtPoint(3, 4000));
    }

    [Fact]
    public void LineGeometry_KeepsEachMoveToAsAPart_AndNegativeDeltas()
    {
        var tile = TestMvt.Encode(new TestMvt.Layer("roads",
            [new TestMvt.Feature(TestMvt.LineString, [[[100, 100], [50, 120], [10, 10]], [[-20, 4100], [0, 0]]])]));

        var parts = MvtTileDecoder.Decode(tile, 10).Single().Features.Single().Parts;

        parts.Should().HaveCount(2);
        parts[0].Should().Equal(new MvtPoint(100, 100), new MvtPoint(50, 120), new MvtPoint(10, 10));
        parts[1].Should().Equal(new MvtPoint(-20, 4100), new MvtPoint(0, 0));
    }

    [Fact]
    public void PolygonGeometry_EndsARingAtEachClosePath()
    {
        var shell = new[] { new[] { 0, 0 }, [100, 0], [100, 100], [0, 100] };
        var hole = new[] { new[] { 20, 20 }, [20, 40], [40, 40], [40, 20] };
        var tile = TestMvt.Encode(new TestMvt.Layer("water", [TestMvt.Area(shell, hole)]));

        var feature = MvtTileDecoder.Decode(tile, 10).Single().Features.Single();

        feature.GeometryType.Should().Be(MvtGeometryType.Polygon);
        feature.Parts.Should().HaveCount(2);
        feature.Parts[0].Should().HaveCount(4, "a ring doesn't repeat its first vertex");
        feature.Parts[1][2].Should().Be(new MvtPoint(40, 40));
    }

    [Fact]
    public void Properties_ResolveTagsToKeysAndValues_EveryValueTypeAsInvariantText()
    {
        var properties = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["name"] = "Rua Augusta",
            ["lanes"] = 2L,
            ["offset"] = -3L,
            ["width"] = 7.25d,
            ["speed"] = 1.5f,
            ["oneway"] = true,
        };
        var tile = TestMvt.Encode(new TestMvt.Layer("roads", [TestMvt.PointAt(1, 1, properties, id: 42)]));

        var feature = MvtTileDecoder.Decode(tile, 10).Single().Features.Single();

        feature.Id.Should().Be(42UL);
        feature.Properties.Should().BeEquivalentTo(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = "Rua Augusta",
            ["lanes"] = "2",
            ["offset"] = "-3",
            ["width"] = "7.25",
            ["speed"] = "1.5",
            ["oneway"] = "true",
        });
    }

    [Fact]
    public void AFeatureWithoutAnId_HasNone()
    {
        var tile = TestMvt.Encode(new TestMvt.Layer("pois", [TestMvt.PointAt(1, 1)]));

        MvtTileDecoder.Decode(tile, 10).Single().Features.Single().Id.Should().BeNull();
    }

    [Fact]
    public void Decoding_StopsAtMaxFeatures_AcrossLayers()
    {
        var tile = TestMvt.Encode(
            new TestMvt.Layer("a", [TestMvt.PointAt(1, 1), TestMvt.PointAt(2, 2), TestMvt.PointAt(3, 3)]),
            new TestMvt.Layer("b", [TestMvt.PointAt(4, 4)]));

        var layers = MvtTileDecoder.Decode(tile, maxFeatures: 2);

        layers.Should().ContainSingle().Which.Features.Should().HaveCount(2);
    }

    [Fact]
    public void ZeroMaxFeatures_DecodesNothing()
    {
        var tile = TestMvt.Encode(new TestMvt.Layer("a", [TestMvt.PointAt(1, 1)]));

        MvtTileDecoder.Decode(tile, 0).Should().BeEmpty();
    }

    [Fact]
    public void AnEmptyTile_HasNoLayers() =>
        MvtTileDecoder.Decode([], 10).Should().BeEmpty();

    [Fact]
    public void ATruncatedTile_IsAFormatError()
    {
        var tile = TestMvt.Encode(new TestMvt.Layer("roads", [TestMvt.Line([0, 0], [10, 20])]));

        var act = () => MvtTileDecoder.Decode(tile[..^3], 10);

        act.Should().Throw<PMTilesFormatException>();
    }

    [Fact]
    public void AnUnknownGeometryCommand_IsAFormatError()
    {
        // Layer "x" with one feature whose geometry is the single command id 5 (unknown), count 1.
        byte[] feature = [0x18, 0x01, 0x22, 0x01, 0x0D];
        byte[] layer = [0x0A, 0x01, (byte)'x', 0x12, (byte)feature.Length, .. feature];
        byte[] tile = [0x1A, (byte)layer.Length, .. layer];

        var act = () => MvtTileDecoder.Decode(tile, 10);

        act.Should().Throw<PMTilesFormatException>().WithMessage("*unknown command 5*");
    }

    [Fact]
    public void ATagPastTheLayersKeys_IsAFormatError()
    {
        // One feature tagged (key 3, value 0) in a layer with no keys or values.
        byte[] feature = [0x12, 0x02, 0x03, 0x00, 0x18, 0x01, 0x22, 0x03, 0x09, 0x02, 0x02];
        byte[] layer = [0x0A, 0x01, (byte)'x', 0x12, (byte)feature.Length, .. feature];
        byte[] tile = [0x1A, (byte)layer.Length, .. layer];

        var act = () => MvtTileDecoder.Decode(tile, 10);

        act.Should().Throw<PMTilesFormatException>().WithMessage("*past its layer's keys*");
    }
}
