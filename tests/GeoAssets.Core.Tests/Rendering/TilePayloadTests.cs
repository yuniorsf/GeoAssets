using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

/// <summary>XD01-431 — <see cref="TilePayload"/>, <see cref="TileLayerSet"/>, <see cref="TileRequest"/>, <see cref="TileFormats"/>.</summary>
public class TilePayloadTests
{
    // ── TilePayload ─────────────────────────────────────────────────────────

    [Fact]
    public void Payload_KeepsItsParts()
    {
        var payload = new TilePayload(new byte[] { 1, 2, 3 }, TileFormats.MapboxVectorTileContentType, "gzip");

        payload.Content.ToArray().Should().Equal(1, 2, 3);
        payload.ContentType.Should().Be("application/vnd.mapbox-vector-tile");
        payload.ContentEncoding.Should().Be("gzip");
        payload.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Empty_HasNoContent()
    {
        var empty = TilePayload.Empty(TileFormats.PngContentType);

        empty.IsEmpty.Should().BeTrue();
        empty.ContentType.Should().Be("image/png");
        empty.ContentEncoding.Should().BeNull();
    }

    [Fact]
    public void ETag_IsAQuotedContentHash()
    {
        var payload = new TilePayload(new byte[] { 1, 2, 3 }, TileFormats.PngContentType);

        payload.ETag.Should().MatchRegex("^\"[0-9a-f]{32}\"$");
        payload.ETag.Should().BeSameAs(payload.ETag);
    }

    [Fact]
    public void ETag_DependsOnTheBytesAndEncodingAlone()
    {
        var a = new TilePayload(new byte[] { 1, 2, 3 }, TileFormats.PngContentType);
        var sameBytes = new TilePayload(new byte[] { 1, 2, 3 }, "image/x-other");
        var otherBytes = new TilePayload(new byte[] { 1, 2, 4 }, TileFormats.PngContentType);
        var gzipped = new TilePayload(new byte[] { 1, 2, 3 }, TileFormats.PngContentType, "gzip");

        sameBytes.ETag.Should().Be(a.ETag);
        otherBytes.ETag.Should().NotBe(a.ETag);
        gzipped.ETag.Should().NotBe(a.ETag);
        TilePayload.Empty("image/png").ETag.Should().NotBe(a.ETag);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Payload_RefusesABlankContentTypeOrEncoding(string blank)
    {
        var type = () => new TilePayload(ReadOnlyMemory<byte>.Empty, blank);
        var encoding = () => new TilePayload(ReadOnlyMemory<byte>.Empty, "image/png", blank);

        type.Should().Throw<ArgumentException>();
        encoding.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Payload_RefusesANullContentType()
    {
        var act = () => new TilePayload(ReadOnlyMemory<byte>.Empty, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── TileFormats ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(TileFormat.Mvt, "application/vnd.mapbox-vector-tile", "mvt")]
    [InlineData(TileFormat.Png, "image/png", "png")]
    public void Formats_MapToTheirMediaTypeAndExtension(TileFormat format, string contentType, string extension)
    {
        format.ContentType().Should().Be(contentType);
        format.FileExtension().Should().Be(extension);
    }

    [Fact]
    public void Formats_RefuseAnUndefinedValue()
    {
        var type = () => ((TileFormat)42).ContentType();
        var extension = () => ((TileFormat)42).FileExtension();

        type.Should().Throw<ArgumentOutOfRangeException>();
        extension.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── TileLayerSet / TileRequest ──────────────────────────────────────────

    [Fact]
    public void LayerSet_KeepsTheLayersInOrder()
    {
        var set = new TileLayerSet("network", ["cables", "poles", "manholes"]);

        set.Id.Should().Be("network");
        set.LayerIds.Should().Equal("cables", "poles", "manholes");
    }

    [Fact]
    public void LayerSet_RefusesNoLayers_DuplicatesAndBadIds()
    {
        var none = () => new TileLayerSet("network", []);
        var duplicate = () => new TileLayerSet("network", ["poles", "cables", "poles"]);
        var badLayer = () => new TileLayerSet("network", ["po/les"]);
        var badId = () => new TileLayerSet("net work", ["poles"]);
        var nullLayers = () => new TileLayerSet("network", null!);
        var nullLayer = () => new TileLayerSet("network", [null!]);

        none.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("layerIds");
        duplicate.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("layerIds");
        badLayer.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("layerIds");
        badId.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("id");
        nullLayers.Should().Throw<ArgumentNullException>();
        nullLayer.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void LayerSet_ComparesLayersOrdinally()
    {
        var set = new TileLayerSet("network", ["Poles", "poles"]);

        set.LayerIds.Should().HaveCount(2);
    }

    [Fact]
    public void Request_KeepsItsParts_AndRefusesANullLayerSet()
    {
        var set = new TileLayerSet("network", ["poles"]);
        var request = new TileRequest(set, new TileAddress(3, 1, 2));

        request.LayerSet.Should().BeSameAs(set);
        request.Address.Should().Be(new TileAddress(3, 1, 2));
        request.Should().Be(new TileRequest(set, new TileAddress(3, 1, 2)));

        var act = () => new TileRequest(null!, default);
        act.Should().Throw<ArgumentNullException>();
    }
}
