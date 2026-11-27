using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Server.Rendering;
using Microsoft.Net.Http.Headers;
using SkiaSharp;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// The authenticated tile endpoints (ADR-002 D6, XD01-434), all requiring <c>features:read</c> (as WMS does, XD01-96) and
/// answering in the caller's tenant scope; none loads the caller's organization catalog first
/// (<see cref="OrganizationAssetCatalogHostExtensions.WithoutOrganizationCatalogWarmup{TBuilder}"/>: tiles read PostGIS,
/// never the full-catalog <c>WarmCacheAsync</c>).
/// <list type="bullet">
///   <item><c>GET /tiles/style.json[?layers={set}]</c> — the MapLibre style of a layer set (default <c>assets</c>,
///   <see cref="TileStyleDocument"/>), whose URLs carry the current <c>{v}</c>; <c>Cache-Control: private, no-cache</c>
///   with an <c>ETag</c> (a client re-reads it to learn a new <c>{v}</c>).</item>
///   <item><c>GET /tiles/{set}/{v}/{z}/{x}/{y}.mvt</c> — a Mapbox Vector Tile of the set's vector/aggregate-tier layers
///   at that zoom (<see cref="IVectorTileSource"/>), gzip-encoded once when drawn (<c>Content-Encoding: gzip</c>,
///   <c>Vary: Accept-Encoding</c>; decoded for a client that refuses gzip — the response compression middleware never
///   touches it). An empty tile is <c>204 No Content</c>, which MapLibre takes as an empty tile.</item>
///   <item><c>GET /tiles/{set}/{v}/{z}/{x}/{y}.png</c> — a 256 px PNG of the set's raster-tier layers
///   (<see cref="IRasterTileSource"/>; a metatile source's whole block is drawn once and cached). An empty tile is a
///   transparent 256 px PNG (<c>200</c>): MapLibre's raster sources expect an image.</item>
/// </list>
/// <para><b>Status codes.</b> 400 for a set or <c>{v}</c> outside <see cref="TileKeySegment"/>, or a zoom/column/row
/// outside the pyramid; 401 without a caller, 403 without <c>features:read</c>; 404 for a set the caller does not see;
/// 304 when <c>If-None-Match</c> holds the tile's <c>ETag</c> (content-derived, <see cref="TilePayload.ETag"/>).</para>
/// <para><b>Versions.</b> <c>{v}</c> is the caller's organization's (<see cref="TileVersions.GenerationOf"/>, XD01-458): a
/// write changes it only for the scopes that can see it. Shared across replicas (<see cref="TileServing.Sync"/>), a request
/// first makes its organization's versions current (<see cref="TileVersionSync.EnsureCurrentAsync"/>: read once).</para>
/// <para><b>Caching.</b> A tile whose <c>{v}</c> is current (<see cref="TileVersions.IsCurrent(string, string, Guid)"/>) is
/// <c>Cache-Control: private, max-age=31536000, immutable</c>; any other <c>{v}</c> is answered with the current tile
/// and <c>private, no-cache</c>. Server side, tiles up to <see cref="TileServingOptions.MaxCachedZoom"/> are kept in the
/// <see cref="ITileCache"/> under <see cref="TileCacheKey"/>(caller's <see cref="TileScopeResolver">organization + grant
/// hash</see>, set, cache version, tile, format) — two callers whose scopes differ never share an entry. A tile drawn
/// while a write was invalidated is dropped again (the generation moved), so it never outlives the write's eviction.</para>
/// </summary>
public static class TileEndpointExtensions
{
    /// <summary>The route prefix.</summary>
    public const string Prefix = "/tiles";

    /// <summary>A tile whose <c>{v}</c> is current: per caller, for a year, never revalidated.</summary>
    public const string ImmutableCacheControl = "private, max-age=31536000, immutable";

    /// <summary>A tile under another <c>{v}</c>, and <c>style.json</c>: per caller, revalidated on every use.</summary>
    public const string RevalidateCacheControl = "private, no-cache";

    private const string Gzip = "gzip";

    private static readonly Lazy<byte[]> TransparentPng = new(DrawTransparentPng);

    private static readonly Lazy<string> TransparentPngETag = new(() => new TilePayload(TransparentPng.Value, TileFormats.PngContentType).ETag);

    /// <summary>Maps <c>/tiles/style.json</c>, the <c>.mvt</c>/<c>.png</c> tile routes and <c>/tiles/lod-calibration</c> (<see cref="TileLodCalibrationEndpoint"/>).</summary>
    public static IEndpointRouteBuilder MapTileApi(this IEndpointRouteBuilder routes)
    {
        ArgumentNullException.ThrowIfNull(routes);

        routes.MapGet($"{Prefix}/style.json", (Func<HttpContext, Task<IResult>>)ServeStyleAsync)
            .RequireAuthorization("features:read")
            .WithoutOrganizationCatalogWarmup();

        routes.MapGet($"{Prefix}/{{layer}}/{{v}}/{{z}}/{{x}}/{{y}}.mvt", (HttpContext http, string layer, string v, string z, string x, string y) =>
                ServeTileAsync(http, layer, v, z, x, y, TileFormat.Mvt))
            .RequireAuthorization("features:read")
            .WithoutOrganizationCatalogWarmup();

        routes.MapGet($"{Prefix}/{{layer}}/{{v}}/{{z}}/{{x}}/{{y}}.png", (HttpContext http, string layer, string v, string z, string x, string y) =>
                ServeTileAsync(http, layer, v, z, x, y, TileFormat.Png))
            .RequireAuthorization("features:read")
            .WithoutOrganizationCatalogWarmup();

        // The on-demand LOD calibration (XD01-439): features:read + organizations:edit, recommendations only.
        routes.MapTileLodCalibration();

        return routes;
    }

    private static async Task<IResult> ServeStyleAsync(HttpContext http)
    {
        var layer = http.Request.Query["layers"].ToString();
        if (layer.Length == 0) layer = TileLayerSetDescriptor.AllAssetTypes;
        if (!TileKeySegment.IsValid(layer))
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid layer set", detail: "'layers' must be a tile key segment.");

        var serving = http.RequestServices.GetRequiredService<TileServing>();
        var catalog = http.RequestServices.GetRequiredService<IAssetProvider>();
        var descriptor = TileLayerSetDescriptor.Resolve(layer, catalog, serving.Lod);
        if (descriptor is null) return Results.NotFound();

        var organization = OrganizationOf(http);
        try
        {
            if (serving.Sync is { } sync) await sync.EnsureCurrentAsync(organization, http.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            return Results.Empty;
        }

        var version = serving.Versions.PublishedVersion(descriptor.StyleHash, organization);
        var style = TileStyleDocument.Build(descriptor, version, PublicBaseUrl(http.Request), serving.Options.MaxCachedZoom);
        var body = JsonSerializer.SerializeToUtf8Bytes(style);
        var etag = "\"" + Convert.ToHexStringLower(SHA256.HashData(body).AsSpan(0, 16)) + "\"";

        var headers = http.Response.Headers;
        headers.CacheControl = RevalidateCacheControl;
        headers.ETag = etag;
        return Matches(http.Request, etag)
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : Results.Bytes(body, "application/json");
    }

    private static async Task<IResult> ServeTileAsync(HttpContext http, string layer, string v, string z, string x, string y, TileFormat format)
    {
        if (!TileKeySegment.IsValid(layer) || !TileKeySegment.IsValid(v))
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid tile URL", detail: "The layer set and version must be tile key segments.");
        if (!TryParseAddress(z, x, y, out var address))
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid tile address", detail: "The zoom, column and row must name a tile of the pyramid.");

        var services = http.RequestServices;
        var serving = services.GetRequiredService<TileServing>();
        var descriptor = TileLayerSetDescriptor.Resolve(layer, services.GetRequiredService<IAssetProvider>(), serving.Lod);
        if (descriptor is null) return Results.NotFound();

        var ct = http.RequestAborted;
        var organization = OrganizationOf(http);
        try
        {
            if (serving.Sync is { } sync) await sync.EnsureCurrentAsync(organization, ct).ConfigureAwait(false);
            var current = serving.Versions.IsCurrent(v, descriptor.StyleHash, organization);
            var (payload, cache) = await TileAsync(http, serving, descriptor, address, format, ct).ConfigureAwait(false);
            return new TileResult(payload, format, current, cache);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Results.Empty;
        }
    }

    private static async Task<(TilePayload Payload, string Cache)> TileAsync(
        HttpContext http, TileServing serving, TileLayerSetDescriptor descriptor, TileAddress address, TileFormat format, CancellationToken ct)
    {
        var layerSet = descriptor.LayerSetAt(format, address.Z);
        if (layerSet is null) return (TilePayload.Empty(format.ContentType()), TileMetrics.Uncached);

        var services = http.RequestServices;
        var scope = await TileScopeResolver.ResolveAsync(
            services.GetService<ITenantContext>(), services.GetService<IOrganizationGrantRepository>(), ct).ConfigureAwait(false);
        var cacheVersion = serving.Versions.CacheVersion(descriptor.StyleHash);
        var key = new TileCacheKey(scope, descriptor.Id, cacheVersion, address, format);
        var cacheable = address.Z <= serving.Options.MaxCachedZoom;

        if (cacheable && await serving.Cache.GetAsync(key, ct).ConfigureAwait(false) is { } cached)
            return (cached, TileMetrics.Hit);

        var request = new TileRequest(layerSet, address);
        var started = serving.TimeProvider.GetTimestamp();
        var drawing = format == TileFormat.Mvt
            ? await DrawVectorAsync(services.GetRequiredService<IVectorTileSource>(), serving, request, ct).ConfigureAwait(false)
            : await DrawRasterAsync(services.GetRequiredService<IRasterTileSource>(), serving, key, request, ct).ConfigureAwait(false);
        TileMetrics.Rendered(format.FileExtension(), serving.TimeProvider.GetElapsedTime(started).TotalMilliseconds);

        if (!cacheable) return (drawing.Tiles[address], TileMetrics.Uncached);

        // Bookkeeping of finished work: never cut short by the caller going away.
        foreach (var (tile, payload) in drawing.Tiles)
            await serving.Cache.SetAsync(new TileCacheKey(scope, descriptor.Id, cacheVersion, tile, format), payload, CancellationToken.None).ConfigureAwait(false);

        // A write invalidated since the drawing began: what it drew may predate the write, so it must not stay cached.
        if (serving.Versions.Generation != drawing.Generation)
        {
            foreach (var tile in drawing.Tiles.Keys)
                await serving.Cache.RemoveAsync(TileRange.Of(tile), CancellationToken.None).ConfigureAwait(false);
        }

        return (drawing.Tiles[address], TileMetrics.Miss);
    }

    private static async Task<TileDrawing> DrawVectorAsync(IVectorTileSource source, TileServing serving, TileRequest request, CancellationToken ct)
    {
        var generation = serving.Versions.Generation;
        var tile = await source.GetVectorTileAsync(request, ct).ConfigureAwait(false);
        return new TileDrawing(new Dictionary<TileAddress, TilePayload> { [request.Address] = Encode(tile) }, generation);
    }

    private static async Task<TileDrawing> DrawRasterAsync(
        IRasterTileSource source, TileServing serving, TileCacheKey key, TileRequest request, CancellationToken ct)
    {
        if (source is not IRasterMetatileSource metatiles)
        {
            var generation = serving.Versions.Generation;
            var tile = await source.GetRasterTileAsync(request, ct).ConfigureAwait(false);
            return new TileDrawing(new Dictionary<TileAddress, TilePayload> { [request.Address] = tile }, generation);
        }

        // A request joining a drawing under way takes the generation that drawing began at, not its own.
        return await serving.DrawOnceAsync(
            DrawingKey(key, request, metatiles.MetatileSpan),
            async () =>
            {
                var generation = serving.Versions.Generation;
                var tiles = await metatiles.GetMetatileAsync(request, ct).ConfigureAwait(false);
                return new TileDrawing(tiles, generation);
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>The caller's organization, as <see cref="TileScopeResolver"/> reads it (<see cref="Guid.Empty"/> for an organization-less caller).</summary>
    private static Guid OrganizationOf(HttpContext http) =>
        http.RequestServices.GetService<ITenantContext>()?.CurrentOrganizationId ?? Guid.Empty;

    /// <summary>One drawing per scope, set, version, metatile and layers drawn.</summary>
    private static string DrawingKey(TileCacheKey key, TileRequest request, int metatileSpan) =>
        string.Join(
            '/',
            key.Scope.OrganizationScope,
            key.Scope.GrantScopeHash,
            key.LayerSetId,
            key.LayerVersion,
            request.Address.Metatile(metatileSpan).ToString(),
            string.Join(',', request.LayerSet.LayerIds));

    /// <summary>A vector tile as cached and served: gzip-encoded once (an empty one stays empty).</summary>
    internal static TilePayload Encode(TilePayload tile)
    {
        if (tile.IsEmpty || tile.ContentEncoding is not null) return tile;

        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(tile.Content.Span);

        return new TilePayload(buffer.ToArray(), tile.ContentType, Gzip);
    }

    private static byte[] Decode(TilePayload tile)
    {
        using var input = new MemoryStream(tile.Content.ToArray());
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static bool TryParseAddress(string z, string x, string y, out TileAddress address)
    {
        address = default;
        return int.TryParse(z, NumberStyles.None, CultureInfo.InvariantCulture, out var zoom)
            && int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var column)
            && int.TryParse(y, NumberStyles.None, CultureInfo.InvariantCulture, out var row)
            && TileAddress.TryCreate(zoom, column, row, out address);
    }

    /// <summary>Whether <c>If-None-Match</c> names <paramref name="etag"/> (or <c>*</c>).</summary>
    private static bool Matches(HttpRequest request, string etag)
    {
        var tags = request.GetTypedHeaders().IfNoneMatch;
        if (tags is null || tags.Count == 0) return false;

        var own = new EntityTagHeaderValue(etag);
        return tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(own, useStrongComparison: false));
    }

    private static bool AcceptsGzip(HttpRequest request) =>
        request.GetTypedHeaders().AcceptEncoding.Any(e =>
            (e.Value.Equals(Gzip, StringComparison.OrdinalIgnoreCase) || e.Value.Equals("*", StringComparison.Ordinal))
            && e.Quality is null or > 0);

    /// <summary>
    /// The origin a client reaches this host at: the gateway's <c>X-Forwarded-Proto</c>/<c>Host</c>/<c>Prefix</c> (it
    /// overwrites whatever a caller sent) when present, else the request's own scheme, host and path base.
    /// </summary>
    internal static string PublicBaseUrl(HttpRequest request)
    {
        var headers = request.Headers;
        var proto = First(headers["X-Forwarded-Proto"]);
        var host = First(headers["X-Forwarded-Host"]);
        if (proto is "http" or "https" && host is not null
            && Uri.TryCreate($"{proto}://{host}", UriKind.Absolute, out var origin) && string.Equals(origin.PathAndQuery, "/", StringComparison.Ordinal))
        {
            var prefix = First(headers["X-Forwarded-Prefix"]) is { } p && p.StartsWith('/') ? p.TrimEnd('/') : string.Empty;
            return origin.GetLeftPart(UriPartial.Authority) + (prefix.Length == 0 ? string.Empty : new PathString(prefix).ToUriComponent());
        }

        return $"{request.Scheme}://{request.Host.ToUriComponent()}{request.PathBase.ToUriComponent()}";
    }

    private static string? First(Microsoft.Extensions.Primitives.StringValues values)
    {
        var value = values.ToString();
        if (value.Length == 0) return null;

        var comma = value.IndexOf(',', StringComparison.Ordinal);
        return (comma < 0 ? value : value[..comma]).Trim();
    }

    private static byte[] DrawTransparentPng()
    {
        using var bitmap = new SKBitmap(SkiaRasterTileSource.TileSize, SkiaRasterTileSource.TileSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.Transparent);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>A tile response: headers, conditional request, empty-tile and content-coding handling.</summary>
    private sealed class TileResult(TilePayload payload, TileFormat format, bool current, string cache) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var request = httpContext.Request;
            var response = httpContext.Response;
            var headers = response.Headers;
            headers.CacheControl = current ? ImmutableCacheControl : RevalidateCacheControl;

            ReadOnlyMemory<byte> body;
            string etag;
            if (payload.IsEmpty)
            {
                body = format == TileFormat.Png ? TransparentPng.Value : ReadOnlyMemory<byte>.Empty;
                etag = format == TileFormat.Png ? TransparentPngETag.Value : payload.ETag;
            }
            else if (payload.ContentEncoding is null)
            {
                body = payload.Content;
                etag = payload.ETag;
            }
            else
            {
                headers.Vary = HeaderNames.AcceptEncoding;
                if (AcceptsGzip(request) && string.Equals(payload.ContentEncoding, Gzip, StringComparison.OrdinalIgnoreCase))
                {
                    headers.ContentEncoding = payload.ContentEncoding;
                    body = payload.Content;
                    etag = payload.ETag;
                }
                else
                {
                    var decoded = Decode(payload);
                    body = decoded;
                    etag = new TilePayload(decoded, payload.ContentType).ETag;
                }
            }

            headers.ETag = etag;
            if (Matches(request, etag))
            {
                headers.Remove(HeaderNames.ContentEncoding);
                response.StatusCode = StatusCodes.Status304NotModified;
                TileMetrics.Served(format.FileExtension(), cache, 0);
                return;
            }

            if (body.IsEmpty)
            {
                response.StatusCode = StatusCodes.Status204NoContent;
                TileMetrics.Served(format.FileExtension(), cache, 0);
                return;
            }

            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = payload.ContentType;
            response.ContentLength = body.Length;
            TileMetrics.Served(format.FileExtension(), cache, body.Length);
            await response.Body.WriteAsync(body, httpContext.RequestAborted).ConfigureAwait(false);
        }
    }
}
