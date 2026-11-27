using System.IO.Compression;
using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Server.Tiles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// XD01-459 — the deployment-wide keyed sources (a plugin's PMTiles archives and COGs, here <see cref="DescribedTestSource"/>
/// registered as keyed services, as <c>gdal-cloud-native</c> registers them) at <c>/tiles/sources/{id}/{v}/{z}/{x}/{y}.mvt|.png</c>
/// and in <c>style.json</c>: <c>features:read</c>, 404 for an id no keyed source of that format has, the cache headers of
/// the other tile routes under the source's own <c>{v}</c>, one cache entry for every caller, and a style that is the same
/// as before when none is configured.
/// </summary>
public sealed class KeyedTileSourceEndpointTests
{
    private static readonly Guid OrgA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000459");
    private static readonly Guid OrgB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000459");
    private static readonly AssetType Poles = new() { Id = Guid.Parse("00000000-0000-0000-0000-0000000004b1"), Name = "Poles", Color = "#123456", AllowedGeometryType = GeometryType.Point };
    private static readonly TileAddress Tile = new(12, 1503, 2230);

    private static readonly TileSourceDescription BasemapV1 = new("b45e0001", ["roads", "water"], 0, 14, new BoundingBox(-50, -30, -30, -10));
    private static readonly TileSourceDescription OrthoV1 = new("0e7a0001", ["ortho"], 0, 19);

    private static Task<TileTestHost> HostAsync(params (string Key, string Value)[] settings) =>
        TileTestHost.StartAsync(
            [Poles],
            services =>
            {
                services.AddKeyedSingleton<IVectorTileSource>("basemap", (_, _) => new DescribedTestSource("basemap", BasemapV1));
                services.AddKeyedSingleton<IRasterTileSource>("ortho", (_, _) => new DescribedTestSource("ortho", OrthoV1));
                services.AddKeyedSingleton<IVectorTileSource>("plain", (_, _) => new PlainVectorSource());
            },
            settings);

    private static DescribedTestSource Basemap(TileTestHost host) => (DescribedTestSource)host.Services.GetRequiredKeyedService<IVectorTileSource>("basemap");

    private static DescribedTestSource Ortho(TileTestHost host) => (DescribedTestSource)host.Services.GetRequiredKeyedService<IRasterTileSource>("ortho");

    private static Uri SourceUri(string id, string version, TileAddress tile, string extension) =>
        new($"/tiles/sources/{id}/{version}/{tile}.{extension}", UriKind.Relative);

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

    // ── Authorization and input ───────────────────────────────────────────────

    [Theory]
    [InlineData("/tiles/sources/basemap/b45e0001/12/1503/2230.mvt")]
    [InlineData("/tiles/sources/ortho/0e7a0001/12/1503/2230.png")]
    [InlineData("/tiles/sources/nope/v/12/1503/2230.mvt")]
    public async Task AnAnonymousCaller_Is401_AndACallerWithoutFeaturesRead_403(string path)
    {
        await using var host = await HostAsync();

        using var anonymous = await host.ClientFor(organization: null).GetAsync(new Uri(path, UriKind.Relative));
        using var denied = await host.ClientFor(OrgA, withFeaturesRead: false).GetAsync(new Uri(path, UriKind.Relative));

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (Basemap(host).Calls + Ortho(host).Calls).Should().Be(0);
    }

    [Theory]
    [InlineData("/tiles/sources/nope/v/12/0/0.mvt", HttpStatusCode.NotFound)]
    [InlineData("/tiles/sources/nope/v/12/0/0.png", HttpStatusCode.NotFound)]
    [InlineData("/tiles/sources/basemap/v/12/0/0.png", HttpStatusCode.NotFound)]
    [InlineData("/tiles/sources/ortho/v/12/0/0.mvt", HttpStatusCode.NotFound)]
    [InlineData("/tiles/sources/plain/v/12/0/0.mvt", HttpStatusCode.NotFound)]
    [InlineData("/tiles/sources/basemap/v/12/0/0.jpg", HttpStatusCode.NotFound)]
    [InlineData("/tiles/sources/basemap/v/25/0/0.mvt", HttpStatusCode.BadRequest)]
    [InlineData("/tiles/sources/basemap/v/12/4096/0.mvt", HttpStatusCode.BadRequest)]
    [InlineData("/tiles/sources/bas%24map/v/12/0/0.mvt", HttpStatusCode.BadRequest)]
    [InlineData("/tiles/sources/basemap/v%20x/12/0/0.mvt", HttpStatusCode.BadRequest)]
    public async Task AnUnknownIdOrFormat_Is404_AndABadSegmentOrAddress_400(string path, HttpStatusCode expected)
    {
        await using var host = await HostAsync();

        using var response = await host.ClientFor(OrgA).GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(expected);
        (Basemap(host).Calls + Ortho(host).Calls).Should().Be(0);
    }

    // ── Tiles ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AVectorTile_UnderTheSourcesVersion_IsImmutable_GzippedOnce_AskingForAllItsLayers()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);

        using var response = await GetAsync(client, SourceUri("basemap", BasemapV1.Version, Tile, "mvt"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(TileFormats.MapboxVectorTileContentType);
        response.Content.Headers.ContentEncoding.Should().Equal("gzip");
        CacheControl(response).Should().Be(TileEndpointExtensions.ImmutableCacheControl);
        response.Headers.Vary.Should().Contain("Accept-Encoding");
        response.Headers.ETag!.IsWeak.Should().BeFalse();
        var request = Basemap(host).Requests.Single();
        request.LayerSet.Id.Should().Be("basemap");
        request.LayerSet.LayerIds.Should().Equal("roads", "water");
        request.Address.Should().Be(Tile);
        (await GunzipAsync(response)).Should().Equal(DescribedTestSource.Content("basemap", request));

        using var revalidated = await GetAsync(client, SourceUri("basemap", BasemapV1.Version, Tile, "mvt"), ifNoneMatch: response.Headers.ETag.Tag);
        revalidated.StatusCode.Should().Be(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task ARasterTile_UnderTheSourcesVersion_IsAnImmutablePng_AndAnEmptyOneATransparentPng()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);
        var empty = new TileAddress(12, 0, 0);
        Ortho(host).EmptyAt.Add(empty);

        using var response = await GetAsync(client, SourceUri("ortho", OrthoV1.Version, Tile, "png"));
        using var transparent = await GetAsync(client, SourceUri("ortho", OrthoV1.Version, empty, "png"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be(TileFormats.PngContentType);
        response.Content.Headers.ContentEncoding.Should().BeEmpty();
        CacheControl(response).Should().Be(TileEndpointExtensions.ImmutableCacheControl);
        var request = Ortho(host).Requests.First(r => r.Address == Tile);
        request.LayerSet.LayerIds.Should().Equal("ortho");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(DescribedTestSource.Content("ortho", request));
        transparent.StatusCode.Should().Be(HttpStatusCode.OK);
        transparent.Content.Headers.ContentType!.MediaType.Should().Be(TileFormats.PngContentType);
        CacheControl(transparent).Should().Be(TileEndpointExtensions.ImmutableCacheControl);
    }

    [Fact]
    public async Task AnEmptyVectorTile_Is204_WithItsCachingHeaders()
    {
        await using var host = await HostAsync();
        Basemap(host).EmptyAt.Add(Tile);

        using var response = await GetAsync(host.ClientFor(OrgA), SourceUri("basemap", BasemapV1.Version, Tile, "mvt"));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        CacheControl(response).Should().Be(TileEndpointExtensions.ImmutableCacheControl);
    }

    [Fact]
    public async Task AnotherVersion_IsAnsweredWithTheCurrentTile_ButNeverImmutable_AndANewFileIsDrawnAgain()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);
        using (await GetAsync(client, SourceUri("basemap", BasemapV1.Version, Tile, "mvt"))) { }

        using var stale = await GetAsync(client, SourceUri("basemap", "b45e0000", Tile, "mvt"));
        Basemap(host).Description = new TileSourceDescription("b45e0002", BasemapV1.LayerIds, 0, 14);
        using var replaced = await GetAsync(client, SourceUri("basemap", "b45e0002", Tile, "mvt"));
        using var old = await GetAsync(client, SourceUri("basemap", BasemapV1.Version, Tile, "mvt"));

        stale.StatusCode.Should().Be(HttpStatusCode.OK);
        CacheControl(stale).Should().Be(TileEndpointExtensions.RevalidateCacheControl);
        CacheControl(replaced).Should().Be(TileEndpointExtensions.ImmutableCacheControl);
        CacheControl(old).Should().Be(TileEndpointExtensions.RevalidateCacheControl, "a URL under the replaced file's version is no longer current");
        Basemap(host).Calls.Should().Be(2, "the stale URL is answered from the cache, the new file's version is drawn once");
    }

    [Fact]
    public async Task EveryCaller_ReadsTheSameTile_FromOneCacheEntry_WhateverItsOrganization()
    {
        await using var host = await HostAsync();

        foreach (var client in new[] { host.ClientFor(OrgA), host.ClientFor(OrgB), host.ClientFor(null, organizationLess: true) })
        {
            using (client)
            using (var response = await GetAsync(client, SourceUri("basemap", BasemapV1.Version, Tile, "mvt")))
                response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        Basemap(host).Calls.Should().Be(1, "a keyed source is deployment-wide data: one entry, under the deployment scope, for every caller");
        var cached = await host.Cache.GetAsync(
            new TileCacheKey(TileEndpointExtensions.DeploymentScope, "basemap", BasemapV1.Version, Tile, TileFormat.Mvt), CancellationToken.None);
        cached.Should().NotBeNull();
    }

    [Fact]
    public async Task TilesAboveMaxCachedZoom_AreDrawnPerRequest()
    {
        await using var host = await HostAsync(("GeoAssets:Tiles:MaxCachedZoom", "10"));
        using var client = host.ClientFor(OrgA);

        using (await GetAsync(client, SourceUri("ortho", OrthoV1.Version, Tile, "png"))) { }
        using (await GetAsync(client, SourceUri("ortho", OrthoV1.Version, Tile, "png"))) { }

        Ortho(host).Calls.Should().Be(2);
    }

    // ── style.json ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Style_ListsTheKeyedSources_UnderTheirOwnVersions_AndLeavesOutOneThatCannotDescribeItself()
    {
        await using var host = await HostAsync();
        using var client = host.ClientFor(OrgA);

        var style = JsonNode.Parse(await client.GetStringAsync(new Uri("/tiles/style.json", UriKind.Relative)))!;
        Ortho(host).Broken = true;
        var withoutOrtho = JsonNode.Parse(await client.GetStringAsync(new Uri("/tiles/style.json", UriKind.Relative)))!;

        var sources = style["sources"]!.AsObject();
        sources.Select(s => s.Key).Should().Equal("geoassets", "geoassets-source-png-ortho", "geoassets-source-mvt-basemap");
        sources["geoassets-source-mvt-basemap"]!["tiles"]![0]!.GetValue<string>()
            .Should().Be($"http://localhost/tiles/sources/basemap/{BasemapV1.Version}/{{z}}/{{x}}/{{y}}.mvt");
        sources["geoassets-source-png-ortho"]!["tiles"]![0]!.GetValue<string>()
            .Should().Be($"http://localhost/tiles/sources/ortho/{OrthoV1.Version}/{{z}}/{{x}}/{{y}}.png");
        sources.ContainsKey("geoassets-source-mvt-plain").Should().BeFalse("a keyed source that can't describe itself is not served");
        withoutOrtho["sources"]!.AsObject().Select(s => s.Key).Should().Equal("geoassets", "geoassets-source-mvt-basemap");
    }

    [Fact]
    public async Task Style_WithoutKeyedSources_IsTheSameAsBefore()
    {
        await using var plain = await TileTestHost.StartAsync([Poles]);
        await using var keyed = await HostAsync();
        using var plainClient = plain.ClientFor(OrgA);
        using var keyedClient = keyed.ClientFor(OrgA);
        Basemap(keyed).Broken = true;
        Ortho(keyed).Broken = true;

        var without = JsonNode.Parse(await plainClient.GetStringAsync(new Uri("/tiles/style.json", UriKind.Relative)))!;
        var broken = JsonNode.Parse(await keyedClient.GetStringAsync(new Uri("/tiles/style.json", UriKind.Relative)))!;

        without["sources"]!.AsObject().Select(s => s.Key).Should().Equal("geoassets");
        without.ToJsonString().Should().Be(broken.ToJsonString(), "with no keyed source to list, the style is the kernel's own alone");
        plain.Services.GetRequiredService<KeyedTileSources>().VectorIds.Should().BeEmpty();
    }

    private sealed class PlainVectorSource : IVectorTileSource
    {
        public Task<TilePayload> GetVectorTileAsync(TileRequest request, CancellationToken ct) =>
            Task.FromResult(TilePayload.Empty(TileFormats.MapboxVectorTileContentType));
    }
}
