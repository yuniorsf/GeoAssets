using System.Globalization;
using System.Net;
using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NetTopologySuite.IO;
using SkiaSharp;
using Xunit;

namespace GeoAssets.Server.Tests.Wms;

/// <summary>
/// XD01-433 moved the WMS GetMap drawing into <c>SkiaFeaturePainter</c> (shared with the raster tiles); the WMS image must
/// not change. Each GetMap of a fixture of every geometry kind — points, lines, polygons with a hole, multi-geometries and a
/// collection, overlapping, partly off the image — is compared pixel for pixel with the pre-extraction renderer
/// (<see cref="PreExtractionWmsRenderer"/>), in EPSG:4326, transparent and opaque, square and not.
/// </summary>
public sealed class WmsRenderingRegressionTests
{
    private static readonly WKTReader Wkt = new();

    private static readonly RenderableFeature[] Fixture =
    [
        Feature("poly", "#2a9d8f", "POLYGON ((-38.9 -13.4, -38.2 -13.4, -38.2 -12.7, -38.9 -12.7, -38.9 -13.4), (-38.7 -13.2, -38.4 -13.2, -38.4 -12.9, -38.7 -12.9, -38.7 -13.2))"),
        Feature("multipoly", "#e9c46a", "MULTIPOLYGON (((-38.95 -12.65, -38.75 -12.65, -38.75 -12.45, -38.95 -12.45, -38.95 -12.65)), ((-38.3 -13.6, -38.05 -13.6, -38.05 -13.3, -38.3 -13.3, -38.3 -13.6)))"),
        Feature("line", "#e76f51", "LINESTRING (-39.2 -13.0, -38.6 -12.8, -38.3 -13.1, -37.8 -12.6)"),
        Feature("multiline", "#264653", "MULTILINESTRING ((-38.9 -13.45, -38.1 -12.55), (-38.9 -12.55, -38.1 -13.45))"),
        Feature("point", "#ff0000", "POINT (-38.5 -13.0)"),
        Feature("edge-point", "#0000ff", "POINT (-39.0 -12.5)"),
        Feature("multipoint", "#8338ec", "MULTIPOINT ((-38.25 -12.75), (-38.75 -13.25), (-38.123 -13.377))"),
        Feature("collection", "#3a86ff", "GEOMETRYCOLLECTION (POINT (-38.6 -12.6), LINESTRING (-38.4 -12.6, -38.2 -12.8))"),
    ];

    [Theory]
    [InlineData(-39, -13.5, -38, -12.5, 256, 256, true)]
    [InlineData(-39, -13.5, -38, -12.5, 256, 256, false)]
    [InlineData(-39.1, -13.6, -37.9, -12.4, 300, 180, true)]
    [InlineData(-38.75, -13.25, -38.25, -12.75, 512, 512, false)]
    public async Task GetMap_DrawsExactlyThePixelsOfThePreExtractionRenderer(
        double minLon, double minLat, double maxLon, double maxLat, int width, int height, bool transparent)
    {
        await using var web = await HostAsync();
        using var client = web.GetTestClient();
        var uri = new Uri(string.Create(
            CultureInfo.InvariantCulture,
            $"/wms?SERVICE=WMS&REQUEST=GetMap&LAYERS=geoassets&BBOX={minLon},{minLat},{maxLon},{maxLat}&WIDTH={width}&HEIGHT={height}&TRANSPARENT={(transparent ? "TRUE" : "FALSE")}"),
            UriKind.Relative);

        using var response = await client.GetAsync(uri);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var expected = PreExtractionWmsRenderer.RenderToPng(Fixture, width, height, minLon, minLat, maxLon, maxLat, transparent);
        using var actualBitmap = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
        using var expectedBitmap = SKBitmap.Decode(expected);
        actualBitmap.Width.Should().Be(width);
        actualBitmap.Height.Should().Be(height);
        actualBitmap.GetPixelSpan().SequenceEqual(expectedBitmap.GetPixelSpan()).Should().BeTrue("the extracted painter draws the WMS image exactly as before");
        using var blankBitmap = SKBitmap.Decode(PreExtractionWmsRenderer.RenderToPng([], width, height, minLon, minLat, maxLon, maxLat, transparent));
        expectedBitmap.GetPixelSpan().SequenceEqual(blankBitmap.GetPixelSpan()).Should().BeFalse("the fixture draws something, so the comparison is not vacuous");
    }

    private static RenderableFeature Feature(string id, string color, string wkt) =>
        new(id, id, "type", GeoGeometry.FromNts(Wkt.Read(wkt)), color);

    private static async Task<WebApplication> HostAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IAssetRenderQuery>(new FixtureRenderQuery());
        builder.Services.AddSingleton<IAssetProvider>(new TestAssetProvider());
        builder.Services.AddAuthorization(options => options.AddPolicy("features:read", policy => policy.RequireAuthenticatedUser()));
        var web = builder.Build();
        web.Use((context, next) =>
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "test-user")], "Test"));
            return next(context);
        });
        web.UseAuthorization();
        web.MapWmsApi();
        await web.StartAsync();
        return web;
    }

    private sealed class FixtureRenderQuery : IAssetRenderQuery
    {
        public Task<IReadOnlyList<RenderableFeature>> GetRenderableAsync(BoundingBox bbox, RenderQueryOptions options, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<RenderableFeature>>(Fixture);

        public Task<IReadOnlyList<FeatureInfo>> GetFeatureInfoAsync(BoundingBox bbox, int maxCount, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FeatureInfo>>([]);
    }
}
