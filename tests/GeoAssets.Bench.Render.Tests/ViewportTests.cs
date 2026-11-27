using FluentAssertions;
using GeoAssets.Core.Rendering;
using Xunit;

namespace GeoAssets.Bench.Render.Tests;

/// <summary>The viewports are Leaflet's, at the zooms and size the baseline documents.</summary>
public sealed class ViewportTests
{
    [Fact]
    public void Viewport_TheWholeWorldAtZoom0_SpansEveryLongitudeAndWebMercatorsLatitudes()
    {
        var viewport = new MapViewport(0, 0, zoom: 0, widthPx: 256, heightPx: 256);

        viewport.Bounds.MinLon.Should().BeApproximately(-180, 1e-9);
        viewport.Bounds.MaxLon.Should().BeApproximately(180, 1e-9);
        viewport.Bounds.MinLat.Should().BeApproximately(-85.0511287798, 1e-6);
        viewport.Bounds.MaxLat.Should().BeApproximately(85.0511287798, 1e-6);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void Viewport_LongitudeSpan_IsTheWidthInPixelsAtThatZoom(int zoom)
    {
        var viewport = new MapViewport(-46.63, -23.55, zoom, 1580, 1020);

        (viewport.Bounds.MaxLon - viewport.Bounds.MinLon).Should().BeApproximately(1580 * 360.0 / (256 * Math.Pow(2, zoom)), 1e-9);
        viewport.Bounds.Contains(-46.63, -23.55).Should().BeTrue();
    }

    [Fact]
    public void Viewport_TheCenterProjectsToTheMiddleOfTheMap_AndTheCornersToItsEdges()
    {
        var viewport = new MapViewport(-46.63, -23.55, 12, 1580, 1020);

        var (cx, cy) = viewport.ToLayerPoint(-46.63, -23.55);
        cx.Should().BeApproximately(790, 1);
        cy.Should().BeApproximately(510, 1);

        var (x0, y0) = viewport.ToLayerPoint(viewport.Bounds.MinLon, viewport.Bounds.MaxLat);
        x0.Should().BeApproximately(0, 1);
        y0.Should().BeApproximately(0, 1);
        var (x1, y1) = viewport.ToLayerPoint(viewport.Bounds.MaxLon, viewport.Bounds.MinLat);
        x1.Should().BeApproximately(1580, 1);
        y1.Should().BeApproximately(1020, 1);
    }

    [Fact]
    public void Viewport_RejectsANegativeZoomOrAnEmptySize()
    {
        FluentActions.Invoking(() => new MapViewport(0, 0, -1, 10, 10)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new MapViewport(0, 0, 1, 0, 10)).Should().Throw<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => new MapViewport(0, 0, 1, 10, 0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Plan_IsTheSameOnEveryRun_DiffersByZoom_AndLooksWhereTheAssetsAre()
    {
        var layout = new DatasetLayout(DatasetGenerator.DefaultSeed);

        var z16 = ViewportPlan.For(layout, DatasetGenerator.DefaultSeed, 16, 12, 1580, 1020);
        var again = ViewportPlan.For(layout, DatasetGenerator.DefaultSeed, 16, 12, 1580, 1020);
        var z12 = ViewportPlan.For(layout, DatasetGenerator.DefaultSeed, 12, 12, 1580, 1020);

        z16.Should().HaveCount(12).And.OnlyContain(v => v.Zoom == 16 && DatasetLayout.Extent.Contains(v.CenterLon, v.CenterLat));
        again.Select(v => (v.CenterLon, v.CenterLat)).Should().Equal(z16.Select(v => (v.CenterLon, v.CenterLat)));
        z12.Select(v => (v.CenterLon, v.CenterLat)).Should().NotEqual(z16.Select(v => (v.CenterLon, v.CenterLat)));
    }

    [Fact]
    public void Plan_StandardZooms_AreOverviewMidAndStreet()
    {
        ViewportPlan.StandardZooms.Should().Equal(8, 12, 16);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void CoveringTiles_AreThe256PxTilesItsPixelsOverlap_AboutFortyAt1580x1020(int zoom)
    {
        var viewport = new MapViewport(-46.63, -23.55, zoom, 1580, 1020);

        var tiles = viewport.CoveringTiles();

        tiles.Zoom.Should().Be(zoom);
        tiles.Width.Should().BeInRange(7, 8, "1580 px is 6.2 tiles wide");
        tiles.Height.Should().BeInRange(4, 5, "1020 px is 4.0 tiles high");
        tiles.Contains(TileAddress.Containing(-46.63, -23.55, zoom)).Should().BeTrue();
        foreach (var corner in new[] { (viewport.Bounds.MinLon + 1e-9, viewport.Bounds.MaxLat - 1e-9), (viewport.Bounds.MaxLon - 1e-9, viewport.Bounds.MinLat + 1e-9) })
            tiles.Contains(TileAddress.Containing(corner.Item1, corner.Item2, zoom)).Should().BeTrue("the viewport's corners are drawn");
    }
}
