using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Core.Tests.Rendering;

/// <summary>XD01-460 — <see cref="LocalVectorTileLayer"/>: a vector tile layer over a valid zoom range of the tile pyramid.</summary>
public sealed class LocalVectorTileLayerTests
{
    private static readonly TileLayerSet Layers = new("pmtiles", ["pmtiles"]);

    [Fact]
    public void ALayer_KeepsWhatItWasGiven_EachSourceLayerOnce()
    {
        var bounds = new BoundingBox(-9.5, 38.6, -9.0, 38.9);

        var layer = new LocalVectorTileLayer(Layers, ["poles", "lines", "poles", "", "Áreas verdes"], 3, 14, bounds);

        layer.LayerSet.Should().BeSameAs(Layers);
        layer.SourceLayers.Should().Equal("poles", "lines", "Áreas verdes");
        layer.MinZoom.Should().Be(3);
        layer.MaxZoom.Should().Be(14);
        layer.Bounds.Should().Be(bounds);
        new LocalVectorTileLayer(Layers, [], 5, 5).Bounds.Should().BeNull();
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(0, TileAddress.MaxZoom + 1)]
    [InlineData(9, 8)]
    public void AZoomRangeOutsideThePyramidOrInverted_IsRefused(int minZoom, int maxZoom)
    {
        var act = () => new LocalVectorTileLayer(Layers, [], minZoom, maxZoom);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ALayerSetAndSourceLayers_AreRequired()
    {
        FluentActions.Invoking(() => new LocalVectorTileLayer(null!, [], 0, 4)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new LocalVectorTileLayer(Layers, null!, 0, 4)).Should().Throw<ArgumentNullException>();
    }
}
