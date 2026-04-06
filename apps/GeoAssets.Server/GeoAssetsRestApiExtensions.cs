using System.Text.Json;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace  GeoAssets.Server;

/// <summary>
/// Maps GeoAssets REST API endpoints onto any <see cref="IEndpointRouteBuilder"/>.
/// The <see cref="IAssetProvider"/> registered in DI is used to serve all requests.
///
/// Typical host setup:
/// <code>
/// builder.Services.AddGeoAssetsPostgres();
/// builder.Services.AddSingleton&lt;IAssetProvider&gt;(sp =>
///     sp.GetRequiredService&lt;IPostgresProviderFactory&gt;()
///       .Create(connectionString));
/// app.MapGeoAssetsApi();
/// </code>
///
/// CORS: add <c>builder.Services.AddCors()</c> and <c>app.UseCors()</c> on the host
/// when the Blazor WASM client is served from a different origin.
/// </summary>
public static class GeoAssetsRestApiExtensions
{
    public static IEndpointRouteBuilder MapGeoAssetsApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/geoassets")
    {
        var opts = GeoJsonSerializer.GetOptions();

        // ── Features ─────────────────────────────────────────────────────────

        routes.MapGet($"{prefix}/features", (IAssetProvider provider) =>
            Results.Json(provider.GetAll(), opts));

        routes.MapGet($"{prefix}/features/bounds",
            async (double minLon, double minLat, double maxLon, double maxLat, IAssetProvider provider) =>
                Results.Json(await provider.GetInBoundsAsync(minLon, minLat, maxLon, maxLat), opts));

        routes.MapGet($"{prefix}/features/{{id}}", (string id, IAssetProvider provider) =>
        {
            var f = provider.GetById(id);
            return f is null ? Results.NotFound() : Results.Json(f, opts);
        });

        routes.MapPost($"{prefix}/features", async (HttpRequest req, IAssetProvider provider) =>
        {
            var feature = await JsonSerializer.DeserializeAsync<GeoFeature>(req.Body, opts);
            if (feature is null) return Results.BadRequest("Invalid feature.");
            provider.Add(feature);
            return Results.Created($"{prefix}/features/{feature.Id}", null);
        });

        routes.MapPut($"{prefix}/features/{{id}}", async (string id, HttpRequest req, IAssetProvider provider) =>
        {
            var feature = await JsonSerializer.DeserializeAsync<GeoFeature>(req.Body, opts);
            if (feature is null) return Results.BadRequest("Invalid feature.");
            feature.Id = id;
            provider.Update(feature);
            return Results.NoContent();
        });

        routes.MapDelete($"{prefix}/features/{{id}}", (string id, IAssetProvider provider) =>
        {
            provider.Delete(id);
            return Results.NoContent();
        });

        routes.MapPost($"{prefix}/features/bulk", async (HttpRequest req, IAssetProvider provider) =>
        {
            var features = await JsonSerializer.DeserializeAsync<GeoFeature[]>(req.Body, opts) ?? [];
            provider.AddRange(features);
            return Results.NoContent();
        });

        routes.MapPost($"{prefix}/features/load", async (HttpRequest req, IAssetProvider provider) =>
        {
            var features = await JsonSerializer.DeserializeAsync<GeoFeature[]>(req.Body, opts) ?? [];
            provider.LoadAll(features);
            return Results.NoContent();
        });

        routes.MapDelete($"{prefix}/features", (IAssetProvider provider) =>
        {
            provider.Clear();
            return Results.NoContent();
        });

        // ── Asset types ───────────────────────────────────────────────────────

        routes.MapGet($"{prefix}/asset-types", (IAssetProvider provider) =>
            Results.Json(provider.GetAssetTypes(), opts));

        routes.MapPost($"{prefix}/asset-types", async (HttpRequest req, IAssetProvider provider) =>
        {
            var assetType = await JsonSerializer.DeserializeAsync<AssetType>(req.Body, opts);
            if (assetType is null) return Results.BadRequest("Invalid asset type.");
            provider.AddAssetType(assetType);
            return Results.Created($"{prefix}/asset-types/{assetType.Id}", null);
        });

        routes.MapDelete($"{prefix}/asset-types/{{id}}", (Guid id, IAssetProvider provider) =>
        {
            provider.DeleteAssetType(id);
            return Results.NoContent();
        });

        return routes;
    }
}
