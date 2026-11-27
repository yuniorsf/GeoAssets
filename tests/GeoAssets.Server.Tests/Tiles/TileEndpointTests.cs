using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Server.Tiles;
using Xunit;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// XD01-434 — <c>/tiles/style.json</c> and <c>/tiles/{set}/{v}/{z}/{x}/{y}.mvt|.png</c> on a host wired as the kernel wires
/// them: authentication and <c>features:read</c>, input validation, unknown sets, immutable caching under the current
/// <c>{v}</c> only, <c>ETag</c>/304, gzip exactly once, empty tiles, the server cache keyed by organization and grants,
/// one drawing per raster metatile, a write during a draw keeping it out of the cache, and no organization catalog warm-up.
/// </summary>
public sealed class TileEndpointTests
{
    private static readonly Guid OrgA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000434");
    private static readonly Guid OrgB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000434");
    private static readonly AssetType Poles = new() { Id = Guid.Parse("00000000-0000-0000-0000-0000000004a1"), Name = "Poles", Color = "#123456", AllowedGeometryType = GeometryType.Point };
    private static readonly string PoleLayer = TileLayerSetDescriptor.LayerIdOf(Poles);
    private static readonly TileAddress Tile = new(12, 1503, 2230);

    private static Task<TileTestHost> HostAsync(params (string Key, string Value)[] settings) => TileTestHost.StartAsync([Poles], settings);

    private static async Task<string> VersionAsync(HttpClient client, string layers = "assets")
    {
        var style = JsonNode.Parse(await client.GetStringAsync(new Uri($"/tiles/style.json?layers={layers}", UriKind.Relative)))!;
        return style["metadata"]!["geoassets:version"]!.GetValue<string>();
    }

    private static Uri TileUri(string version, TileAddress tile, string extension, string set = "assets") =>
        new($"/tiles/{set}/{version}/{tile}.{extension}", UriKind.Relative);

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, Uri uri, bool gzip = true, string? ifNoneMatch = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        if (gzip) request.Headers.AcceptEncoding.ParseAdd("gzip, br");
        if (ifNoneMatch is not null) request.Headers.IfNoneMatch.ParseAdd(ifNoneMatch);
        return await client.SendAsync(request);
    }

    private static string CacheControl(HttpResponseMessage response) => response.Headers.NonValidated["Cache-Control"].ToString();

    private static async Task<byte[]> GunzipAsync(HttpResponseMessage response)
    {
        await using var gzip = new GZipStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        using var output = new MemoryStream();
        await gzip.CopyToAsync(output);
        return output.ToArray();
    }

    // ── Authorization ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/tiles/style.json")]
    [InlineData("/tiles/assets/v/12/1503/2230.mvt")]
    [InlineData("/tiles/assets/v/12/1503/2230.png")]
    public async Task AnAnonymousCaller_Is401_AndACallerWithoutFeaturesRead_403(string path)
    {
        await using var host = await HostAsync();

        using var anonymous = await host.ClientFor(organization: null).GetAsync(new Uri(path, UriKind.Relative));
        using var denied = await host.ClientFor(OrgA, withFeaturesRead: false).GetAsync(new Uri(path, UriKind.Relative));

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Vector.Calls.Should().Be(0);
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/tiles/assets/v/25/0/0.mvt", HttpStatusCode.BadRequest)]
    [InlineData("/tiles/assets/v/12/4096/0.mvt", HttpStatusCode.BadRequest)]
    [InlineData("/tiles/assets/v/12/0/-1.mvt", HttpStatusCode.BadRequest)]
    [InlineData("/tiles/assets/v/12/+1/0.mvt", HttpStatusCode.BadRequest)]
    [InlineData("/tiles/assets/v/z/0/0.png", HttpStatusCode.BadRequest)]
    [InlineData("/tiles/assets/v%20x/12/0/0.mvt", HttpStatusCode.BadRequest)]
    [InlineData("/tiles/as%24ets/v/12/0/0.mvt", HttpStatusCode.BadRequest)]
    [InlineData("/tiles/style.json?layers=a%2Fb", HttpStatusCode.BadRequest)]
    [InlineData("/tiles/roads/v/12/0/0.mvt", HttpStatusCode.NotFound)]
    [InlineData("/tiles/00000000-0000-0000-0000-000000000001/v/12/0/0.png", HttpStatusCode.NotFound)]
    [InlineData("/tiles/style.json?layers=roads", HttpStatusCode.NotFound)]
    [InlineData("/tiles/assets/v/12/0/0.jpg", HttpStatusCode.NotFound)]
    public async Task ABadSegmentZoomOrPosition_Is400_AndASetTheCallerDoesNotSee_404(string path, HttpStatusCode expected)
    {
        await using var host = await HostAsync();

        using var response = await host.ClientFor(OrgA).GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(expected);
        host.Vector.Calls.Should().Be(0);
    }

    // ── Vector tiles ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AVectorTile_UnderTheCurrentVersion_IsImmutable_GzippedOnce_WithAContentETag()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);
        var version = await VersionAsync(client);

        using var response = await GetAsync(client, TileUri(version, Tile, "mvt"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(TileFormats.MapboxVectorTileContentType);
        response.Content.Headers.ContentEncoding.Should().Equal(["gzip"], "the tile is encoded once, by the endpoint, and the compression middleware leaves it");
        CacheControl(response).Should().Be("private, max-age=31536000, immutable");
        response.Headers.Vary.Should().Contain("Accept-Encoding");
        response.Headers.ETag!.IsWeak.Should().BeFalse();
        var body = await GunzipAsync(response);
        var request = host.Vector.Requests.Single();
        body.Should().Equal(RecordingVectorSource.Content(OrgA, request), "one gunzip gives the source's bytes back: no second encoding");
        request.LayerSet.LayerIds.Should().Equal(PoleLayer);
        request.Address.Should().Be(Tile);
    }

    [Fact]
    public async Task AClientRefusingGzip_GetsTheIdentityBytes_UnderAnotherETag()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);
        var version = await VersionAsync(client);

        using var gzipped = await GetAsync(client, TileUri(version, Tile, "mvt"));
        using var identity = await GetAsync(client, TileUri(version, Tile, "mvt"), gzip: false);

        identity.StatusCode.Should().Be(HttpStatusCode.OK);
        identity.Content.Headers.ContentEncoding.Should().BeEmpty();
        (await identity.Content.ReadAsByteArrayAsync()).Should().Equal(await GunzipAsync(gzipped));
        identity.Headers.ETag!.Tag.Should().NotBe(gzipped.Headers.ETag!.Tag, "two content codings are two representations");
        host.Vector.Calls.Should().Be(1);
    }

    [Fact]
    public async Task IfNoneMatch_WithTheTilesETag_Is304_ForBothRepresentations()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);
        var version = await VersionAsync(client);
        using var first = await GetAsync(client, TileUri(version, Tile, "mvt"));
        using var firstIdentity = await GetAsync(client, TileUri(version, Tile, "mvt"), gzip: false);

        using var again = await GetAsync(client, TileUri(version, Tile, "mvt"), ifNoneMatch: first.Headers.ETag!.Tag);
        using var againIdentity = await GetAsync(client, TileUri(version, Tile, "mvt"), gzip: false, ifNoneMatch: firstIdentity.Headers.ETag!.Tag);
        using var other = await GetAsync(client, TileUri(version, Tile, "mvt"), ifNoneMatch: "\"0000\"");

        again.StatusCode.Should().Be(HttpStatusCode.NotModified);
        again.Headers.ETag!.Tag.Should().Be(first.Headers.ETag!.Tag);
        CacheControl(again).Should().Be("private, max-age=31536000, immutable");
        (await again.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
        againIdentity.StatusCode.Should().Be(HttpStatusCode.NotModified);
        other.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnEmptyVectorTile_Is204_WithItsCachingHeaders()
    {
        await using var host = await HostAsync();
        host.Vector.EmptyAt.Add(Tile);
        using var client = host.ClientFor(OrgA);
        var version = await VersionAsync(client);

        using var response = await GetAsync(client, TileUri(version, Tile, "mvt"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.ETag.Should().NotBeNull();
        CacheControl(response).Should().Be("private, max-age=31536000, immutable");
        response.Content.Headers.ContentEncoding.Should().BeEmpty();
    }

    [Fact]
    public async Task AnotherVersion_IsAnsweredWithTheCurrentTile_ButNeverImmutable()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);
        var version = await VersionAsync(client);

        using var current = await GetAsync(client, TileUri(version, Tile, "mvt"));
        using var stale = await GetAsync(client, TileUri("0123456789ab-beef0000-0.0", Tile, "mvt"));

        stale.StatusCode.Should().Be(HttpStatusCode.OK);
        CacheControl(stale).Should().Be("private, no-cache");
        stale.Headers.ETag!.Tag.Should().Be(current.Headers.ETag!.Tag, "it is the same current tile");
        host.Vector.Calls.Should().Be(1, "it is served from the current version's cache entry, not cached under the bogus one");
    }

    [Fact]
    public async Task TheServerCache_AnswersARepeat_AndAnOldGenerationOfTheCurrentStyleStaysImmutable()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);
        var version = await VersionAsync(client);
        using var first = await GetAsync(client, TileUri(version, Tile, "mvt"));

        await host.Invalidation.InvalidateAsync([new DirtyTileRegion(new BoundingBox(10, 10, 10.001, 10.001))], CancellationToken.None);
        var next = await VersionAsync(client);
        using var oldGeneration = await GetAsync(client, TileUri(version, Tile, "mvt"));
        using var newGeneration = await GetAsync(client, TileUri(next, Tile, "mvt"));

        next.Should().NotBe(version, "a committed write changes every {v} style.json publishes");
        host.Vector.Calls.Should().Be(1, "a write elsewhere evicts nothing here: both generations hit the one cached tile");
        CacheControl(oldGeneration).Should().Be("private, max-age=31536000, immutable",
            "an older generation's URL only ever answers bytes at least as new as it names");
        newGeneration.Headers.ETag!.Tag.Should().Be(first.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task AWriteOverTheTile_EvictsIt_SoTheNextRequestDrawsItAgain()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);
        var version = await VersionAsync(client);
        using (await GetAsync(client, TileUri(version, Tile, "mvt"))) { }

        var center = Tile.Envelope4326;
        await host.Invalidation.InvalidateAsync(
            [new DirtyTileRegion(new BoundingBox(center.MinLon + 0.001, center.MinLat + 0.001, center.MinLon + 0.002, center.MinLat + 0.002))],
            CancellationToken.None);
        using (await GetAsync(client, TileUri(await VersionAsync(client), Tile, "mvt"))) { }

        host.Vector.Calls.Should().Be(2);
    }

    [Fact]
    public async Task ATileDrawnWhileAWriteWasInvalidated_IsNotLeftInTheCache()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);
        var version = await VersionAsync(client);
        host.Vector.During = () => host.Invalidation.InvalidateAsync(
            [new DirtyTileRegion(new BoundingBox(-170, -80, -169.999, -79.999))], CancellationToken.None);

        using (await GetAsync(client, TileUri(version, Tile, "mvt"))) { }
        host.Vector.During = null;
        using (await GetAsync(client, TileUri(version, Tile, "mvt"))) { }

        host.Vector.Calls.Should().Be(2, "the first draw may predate the write, so it was dropped from the cache");
    }

    [Fact]
    public async Task TilesAboveMaxCachedZoom_AreDrawnPerRequest()
    {
        await using var host = await HostAsync(("GeoAssets:Tiles:MaxCachedZoom", "10"));
        using var client = host.ClientFor(OrgA);
        var version = await VersionAsync(client);

        using (await GetAsync(client, TileUri(version, Tile, "mvt"))) { }
        using var second = await GetAsync(client, TileUri(version, Tile, "mvt"));

        host.Vector.Calls.Should().Be(2);
        CacheControl(second).Should().Be("private, max-age=31536000, immutable");
        host.Cache.Count.Should().Be(0);
    }

    // ── Scopes ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoOrganizations_NeverShareACachedTile()
    {
        await using var host = await HostAsync();
        using var a = host.ClientFor(OrgA);
        using var b = host.ClientFor(OrgB);
        using var shared = host.ClientFor(organization: null, organizationLess: true);

        using var ofA = await GetAsync(a, TileUri(await VersionAsync(a), Tile, "mvt"));
        using var ofB = await GetAsync(b, TileUri(await VersionAsync(b), Tile, "mvt"));
        using var ofShared = await GetAsync(shared, TileUri(await VersionAsync(shared), Tile, "mvt"));

        host.Vector.Calls.Should().Be(3);
        Encoding.UTF8.GetString(await GunzipAsync(ofA)).Should().Contain(OrgA.ToString()).And.NotContain(OrgB.ToString());
        Encoding.UTF8.GetString(await GunzipAsync(ofB)).Should().Contain(OrgB.ToString()).And.NotContain(OrgA.ToString());
        Encoding.UTF8.GetString(await GunzipAsync(ofShared)).Should().Contain(Guid.Empty.ToString());
    }

    [Fact]
    public async Task TwoGrantSets_InOneOrganization_NeverShareACachedTile_AndOneGrantSetShares()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);
        var version = await VersionAsync(client);

        using (await GetAsync(client, TileUri(version, Tile, "mvt"))) { }
        host.Grants.ByGrantee[OrgA] = [new OrganizationGrant { GranteeOrganizationId = OrgA, ResourceOrganizationId = OrgB, AllowedActions = ["features:read"] }];
        using (await GetAsync(client, TileUri(version, Tile, "mvt"))) { }
        using (await GetAsync(client, TileUri(version, Tile, "mvt"))) { }
        host.Grants.ByGrantee[OrgA] = [new OrganizationGrant { GranteeOrganizationId = OrgA, ResourceOrganizationId = OrgB, AllowedActions = ["features:read", "features:write"] }];
        using (await GetAsync(client, TileUri(version, Tile, "mvt"))) { }

        host.Vector.Calls.Should().Be(3, "no grants, one grant, and another grant set are three scopes; the repeat is a hit");
    }

    // ── Raster tiles ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TheFourTilesOfAMetatile_RequestedTogether_CostOneDrawing_AndAreCached()
    {
        await using var host = await HostAsync(("GeoAssets:Tiles:Lod:RasterFromZoom", "0"), ("GeoAssets:Tiles:Lod:VectorFromZoom", "14"));
        using var client = host.ClientFor(OrgA);
        var version = await VersionAsync(client);
        var block = Tile.Metatile(2).EnumerateTiles().ToArray();
        host.Raster.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var requests = block.Select(tile => GetAsync(client, TileUri(version, tile, "png"))).ToArray();
        await Task.Delay(200);
        host.Raster.Gate.SetResult();
        var responses = await Task.WhenAll(requests);
        using (await GetAsync(client, TileUri(version, block[0], "png"))) { }

        host.Raster.Metatiles.Should().Be(1);
        foreach (var (response, tile) in responses.Zip(block))
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
            response.Content.Headers.ContentEncoding.Should().BeEmpty("a PNG is not compressed again");
            Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync()).Should().Be($"png|{tile}");
            response.Dispose();
        }
    }

    [Fact]
    public async Task AnEmptyRasterTile_IsATransparent256PxPng()
    {
        await using var host = await HostAsync(("GeoAssets:Tiles:Lod:RasterFromZoom", "0"), ("GeoAssets:Tiles:Lod:VectorFromZoom", "14"));
        host.Raster.EmptyAt = Tile;
        using var client = host.ClientFor(OrgA);

        using var response = await GetAsync(client, TileUri(await VersionAsync(client), Tile, "png"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        using var bitmap = SkiaSharp.SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
        bitmap.Width.Should().Be(256);
        bitmap.Height.Should().Be(256);
        bitmap.GetPixel(128, 128).Alpha.Should().Be(0);
        response.Headers.ETag.Should().NotBeNull();
    }

    [Fact]
    public async Task EachFormat_CarriesOnlyItsTiersLayers_AndAnEmptyTierNeedsNoSource()
    {
        await using var host = await HostAsync(("GeoAssets:Tiles:Lod:RasterFromZoom", "0"), ("GeoAssets:Tiles:Lod:VectorFromZoom", "14"));
        using var client = host.ClientFor(OrgA);
        var version = await VersionAsync(client);

        using var vectorAtRasterZoom = await GetAsync(client, TileUri(version, Tile, "mvt"));
        using var rasterAtVectorZoom = await GetAsync(client, TileUri(version, new TileAddress(15, 0, 0), "png"));

        vectorAtRasterZoom.StatusCode.Should().Be(HttpStatusCode.NoContent);
        rasterAtVectorZoom.StatusCode.Should().Be(HttpStatusCode.OK, "an empty raster tile is still an image");
        host.Vector.Calls.Should().Be(0);
        host.Raster.Metatiles.Should().Be(0);
    }

    // ── style.json ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Style_IsRevalidated_WithAnETag_AndPublishesForwardedAbsoluteUrls()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/tiles/style.json", UriKind.Relative));
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-Host", "maps.example.com");

        using var response = await client.SendAsync(request);
        var style = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        using var revalidation = new HttpRequestMessage(HttpMethod.Get, new Uri("/tiles/style.json", UriKind.Relative));
        revalidation.Headers.Add("X-Forwarded-Proto", "https");
        revalidation.Headers.Add("X-Forwarded-Host", "maps.example.com");
        revalidation.Headers.IfNoneMatch.ParseAdd(response.Headers.ETag!.Tag);
        using var again = await client.SendAsync(revalidation);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        CacheControl(response).Should().Be("private, no-cache");
        var version = style["metadata"]!["geoassets:version"]!.GetValue<string>();
        version.Should().StartWith(host.Versions.CacheVersion(TileLayerSetDescriptor.Resolve("assets", host.Catalog, new LodPolicy(LodThresholds.VectorOnly))!.StyleHash));
        style["sources"]!["geoassets"]!["tiles"]![0]!.GetValue<string>().Should().Be($"https://maps.example.com/tiles/assets/{version}/{{z}}/{{x}}/{{y}}.mvt");
        style["layers"]!.AsArray().Select(l => l!["id"]!.GetValue<string>()).Should().Equal($"{PoleLayer}-point");
        again.StatusCode.Should().Be(HttpStatusCode.NotModified, "nothing changed since: same body, same ETag");
    }

    [Fact]
    public async Task Style_WithoutForwardedHeaders_UsesTheRequestsOwnOrigin_AndAnAssetTypeSetNamesItsType()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);

        var style = JsonNode.Parse(await client.GetStringAsync(new Uri($"/tiles/style.json?layers={PoleLayer}", UriKind.Relative)))!;

        style["metadata"]!["geoassets:layerSet"]!.GetValue<string>().Should().Be(PoleLayer);
        style["sources"]!["geoassets"]!["tiles"]![0]!.GetValue<string>().Should().StartWith($"http://localhost/tiles/{PoleLayer}/");
    }

    // ── No catalog warm-up ────────────────────────────────────────────────────

    [Fact]
    public async Task TileRequests_NeverLoadTheCallersOrganizationCatalog_OtherRoutesStillDo()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);
        var version = await VersionAsync(client);

        using (await GetAsync(client, TileUri(version, Tile, "mvt"))) { }
        using (await GetAsync(client, TileUri(version, Tile, "png"))) { }
        host.Catalogs.Warmed.Should().Be(0, "tile routes read PostGIS, never WarmCacheAsync's full catalog");

        using (await client.GetAsync(new Uri("/warmed", UriKind.Relative))) { }
        host.Catalogs.Warmed.Should().Be(1, "the middleware still loads it for every other route");
    }
}
