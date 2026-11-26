using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace GeoAssets.Server.Tests.Wms;

/// <summary>
/// The WMS endpoint over the provider abstraction (XD01-372): it reads the request's <see cref="IAssetProvider"/> for
/// its capabilities' layers and the request's <see cref="IAssetRenderQuery"/> for the pixels and the click lookups —
/// nothing else — so it works over any provider's pushdown. Runs a TestServer with a fake query that records what the
/// endpoint asks it, for a signed-in caller holding <c>features:read</c> (which the endpoint requires, XD01-96).
/// </summary>
public sealed class WmsEndpointRenderQueryTests
{
    private const double MinLon = -39.0;
    private const double MinLat = -13.5;
    private const double MaxLon = -38.0;
    private const double MaxLat = -12.5;
    private const int TileSize = 200;

    private sealed class RecordingRenderQuery : IAssetRenderQuery
    {
        public List<(BoundingBox Bbox, RenderQueryOptions Options)> Renders { get; } = [];
        public List<(BoundingBox Bbox, int MaxCount)> Infos { get; } = [];
        public List<RenderableFeature> Features { get; } = [];
        public List<FeatureInfo> Info { get; } = [];
        public bool ThrowCancelled { get; set; }

        public Task<IReadOnlyList<RenderableFeature>> GetRenderableAsync(BoundingBox bbox, RenderQueryOptions options, CancellationToken ct)
        {
            Renders.Add((bbox, options));
            if (ThrowCancelled) throw new OperationCanceledException(ct);
            return Task.FromResult<IReadOnlyList<RenderableFeature>>([.. Features]);
        }

        public Task<IReadOnlyList<FeatureInfo>> GetFeatureInfoAsync(BoundingBox bbox, int maxCount, CancellationToken ct)
        {
            Infos.Add((bbox, maxCount));
            if (ThrowCancelled) throw new OperationCanceledException(ct);
            return Task.FromResult<IReadOnlyList<FeatureInfo>>([.. Info]);
        }
    }

    private sealed class Host(WebApplication web, RecordingRenderQuery query, TestAssetProvider catalog) : IAsyncDisposable
    {
        public RecordingRenderQuery Query { get; } = query;
        public TestAssetProvider Catalog { get; } = catalog;
        public HttpClient Client { get; } = web.GetTestClient();

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await web.StopAsync();
            await web.DisposeAsync();
        }
    }

    private static async Task<Host> HostAsync()
    {
        var query = new RecordingRenderQuery();
        var catalog = new TestAssetProvider();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IAssetRenderQuery>(query);
        builder.Services.AddSingleton<IAssetProvider>(catalog);
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
        return new Host(web, query, catalog);
    }

    private static Uri WmsUri(string request, string layers = "geoassets") =>
        new(string.Create(
            CultureInfo.InvariantCulture,
            $"/wms?SERVICE=WMS&VERSION=1.1.1&{request}&LAYERS={Uri.EscapeDataString(layers)}&BBOX={MinLon},{MinLat},{MaxLon},{MaxLat}&WIDTH={TileSize}&HEIGHT={TileSize}"),
            UriKind.Relative);

    [Fact]
    public async Task GetCapabilities_ListsTheCatalogsAssetTypesAsLayers_AndDoesNotMentionPostGis()
    {
        await using var host = await HostAsync();
        var custom = new AssetType { Name = "Custom render type" };
        host.Catalog.AddAssetType(custom);

        using var response = await host.Client.GetAsync(new Uri("/wms?SERVICE=WMS&REQUEST=GetCapabilities", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var xml = await response.Content.ReadAsStringAsync();
        foreach (var type in host.Catalog.GetAssetTypes())
        {
            xml.Should().Contain($"<Name>geoassets:feature:{type.Id}</Name>");
        }

        xml.Should().Contain($"geoassets:feature:{custom.Id}").And.Contain("Custom render type");
        xml.Should().Contain("<Abstract>OGC WMS 1.1.1</Abstract>");
        xml.ToUpperInvariant().Should().NotContain("POSTGIS");
        host.Query.Renders.Should().BeEmpty();
        host.Query.Infos.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMap_LayersOfAnAssetType_PassTheTypeIdAsTheRenderQueryOption()
    {
        await using var host = await HostAsync();
        var typeId = Guid.NewGuid().ToString();

        using var response = await host.Client.GetAsync(WmsUri("REQUEST=GetMap&FORMAT=image/png", $"geoassets:feature:{typeId}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        var (bbox, options) = host.Query.Renders.Should().ContainSingle().Subject;
        bbox.Should().Be(new BoundingBox(MinLon, MinLat, MaxLon, MaxLat));
        options.AssetTypeId.Should().Be(typeId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(19)]
    public async Task GetMap_LeafletTileOfAZoom_AsksForThatZoom_Capped(int zoom)
    {
        await using var host = await HostAsync();
        var span = 360.0 / Math.Pow(2, zoom);
        var uri = new Uri(string.Create(
            CultureInfo.InvariantCulture,
            $"/wms?SERVICE=WMS&REQUEST=GetMap&LAYERS=geoassets&BBOX={-46.0},{-23.0},{-46.0 + span},{-23.0 + span}&WIDTH=256&HEIGHT=256"),
            UriKind.Relative);

        using var response = await host.Client.GetAsync(uri);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var options = host.Query.Renders.Should().ContainSingle().Subject.Options;
        options.Zoom.Should().Be(zoom, "Leaflet's EPSG:4326 tile of zoom z spans 360 / 2^z degrees over 256 px");
        options.MaxCount.Should().Be(RenderQueryOptions.DefaultMaxCount);
    }

    [Theory]
    [InlineData("-180,-90,180,90", 256, 0)]
    [InlineData("-180,-90,180,90", 64, 0)]
    [InlineData("0,0,0.00001,0.00001", 4096, 24)]
    [InlineData("-39,-13.5,-38,-12.5", 200, 8)]
    public async Task GetMap_AnyOtherScale_AsksForTheNearestZoom_ClampedToTheZoomLevels(string bbox, int width, int expected)
    {
        await using var host = await HostAsync();
        var uri = new Uri($"/wms?SERVICE=WMS&REQUEST=GetMap&LAYERS=geoassets&BBOX={bbox}&WIDTH={width}&HEIGHT={width}", UriKind.Relative);

        using var response = await host.Client.GetAsync(uri);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Query.Renders.Should().ContainSingle().Which.Options.Zoom.Should().Be(expected);
    }

    [Fact]
    public async Task GetMap_BoxWithoutWidth_AsksWithoutAZoom()
    {
        await using var host = await HostAsync();

        using var response = await host.Client.GetAsync(new Uri("/wms?SERVICE=WMS&REQUEST=GetMap&BBOX=-38,-13,-38,-12&WIDTH=256&HEIGHT=256", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Query.Renders.Should().ContainSingle().Which.Options.Zoom.Should().BeNull();
    }

    [Fact]
    public async Task GetMap_PlainLayer_AsksForEveryAssetType()
    {
        await using var host = await HostAsync();

        using var response = await host.Client.GetAsync(WmsUri("REQUEST=GetMap&FORMAT=image/png"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Query.Renders.Should().ContainSingle().Which.Options.AssetTypeId.Should().BeNull();
    }

    [Fact]
    public async Task GetMap_PaintsAFeatureInTheColorTheQueryResolved_AtItsPixel()
    {
        await using var host = await HostAsync();
        host.Query.Features.Add(new RenderableFeature("pole", "Pole", "type", new GeoPoint(-38.5, -13.0), "#ff0000"));

        using var response = await host.Client.GetAsync(WmsUri("REQUEST=GetMap&FORMAT=image/png&TRANSPARENT=TRUE"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var bitmap = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
        bitmap.Width.Should().Be(TileSize);
        bitmap.Height.Should().Be(TileSize);
        var painted = bitmap.GetPixel(TileSize / 2, TileSize / 2);
        painted.Alpha.Should().BeGreaterThan(0);
        painted.Red.Should().BeGreaterThan(200);
        painted.Green.Should().BeLessThan(60);
        painted.Blue.Should().BeLessThan(60);
        bitmap.GetPixel(5, 5).Alpha.Should().Be(0, "away from the feature the transparent tile stays empty");
    }

    [Fact]
    public async Task GetFeatureInfo_PassesTheOnePercentBoxAroundTheClick_AndFeatureCount()
    {
        await using var host = await HostAsync();
        host.Query.Info.Add(new FeatureInfo("pole-1", "Pole 1", "type", "first"));

        using var response = await host.Client.GetAsync(WmsUri("REQUEST=GetFeatureInfo&I=100&J=50&FEATURE_COUNT=7&INFO_FORMAT=application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var clickLon = MinLon + (100.0 / TileSize * (MaxLon - MinLon));
        var clickLat = MaxLat - (50.0 / TileSize * (MaxLat - MinLat));
        var (bbox, maxCount) = host.Query.Infos.Should().ContainSingle().Subject;
        maxCount.Should().Be(7);
        bbox.MinLon.Should().BeApproximately(clickLon - 0.01, 1e-9);
        bbox.MaxLon.Should().BeApproximately(clickLon + 0.01, 1e-9);
        bbox.MinLat.Should().BeApproximately(clickLat - 0.01, 1e-9);
        bbox.MaxLat.Should().BeApproximately(clickLat + 0.01, 1e-9);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var feature = json.RootElement.GetProperty("features").EnumerateArray().Should().ContainSingle().Subject;
        feature.GetProperty("id").GetString().Should().Be("pole-1");
        feature.GetProperty("name").GetString().Should().Be("Pole 1");
        feature.GetProperty("assetTypeId").GetString().Should().Be("type");
        feature.GetProperty("description").GetString().Should().Be("first");
    }

    [Fact]
    public async Task GetFeatureInfo_WithoutFeatureCount_AsksForOneFeature()
    {
        await using var host = await HostAsync();

        using var response = await host.Client.GetAsync(WmsUri("REQUEST=GetFeatureInfo&I=10&J=10"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        host.Query.Infos.Should().ContainSingle().Which.MaxCount.Should().Be(1);
    }

    [Theory]
    [InlineData("REQUEST=GetMap&FORMAT=image/png")]
    [InlineData("REQUEST=GetFeatureInfo&I=10&J=10")]
    public async Task CancelledQuery_AnswersAOneByOnePng(string request)
    {
        await using var host = await HostAsync();
        host.Query.ThrowCancelled = true;

        using var response = await host.Client.GetAsync(WmsUri(request));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        using var bitmap = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
        bitmap.Width.Should().Be(1);
        bitmap.Height.Should().Be(1);
    }
}
