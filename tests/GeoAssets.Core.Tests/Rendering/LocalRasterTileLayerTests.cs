using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

/// <summary>XD01-465 — <see cref="LocalRasterTileLayer"/>: an image layer over a valid zoom range of the tile pyramid.</summary>
public sealed class LocalRasterTileLayerTests
{
    private static readonly TileLayerSet Layers = new("pmtiles", ["pmtiles"]);

    [Fact]
    public void ALayer_KeepsWhatItWasGiven()
    {
        var bounds = new BoundingBox(-9.5, 38.6, -9.0, 38.9);

        var layer = new LocalRasterTileLayer(Layers, "image/webp", 3, 14, bounds);

        layer.LayerSet.Should().BeSameAs(Layers);
        layer.ContentType.Should().Be("image/webp");
        layer.MinZoom.Should().Be(3);
        layer.MaxZoom.Should().Be(14);
        layer.Bounds.Should().Be(bounds);
        new LocalRasterTileLayer(Layers, "image/png", 5, 5).Bounds.Should().BeNull();
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(0, TileAddress.MaxZoom + 1)]
    [InlineData(9, 8)]
    public void AZoomRangeOutsideThePyramidOrInverted_IsRefused(int minZoom, int maxZoom)
    {
        var act = () => new LocalRasterTileLayer(Layers, "image/png", minZoom, maxZoom);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(TileFormats.MapboxVectorTileContentType)]
    public void AContentTypeThatIsNotAnImage_IsRefused(string contentType)
    {
        var act = () => new LocalRasterTileLayer(Layers, contentType, 0, 4);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ALayerSet_IsRequired()
    {
        var act = () => new LocalRasterTileLayer(null!, "image/png", 0, 4);

        act.Should().Throw<ArgumentNullException>();
    }
}
