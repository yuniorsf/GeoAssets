using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Server.Tests.FeatureImport;

internal static class FeatureImportRequests
{
    public const string ServiceCaller = GeoAuthorizationHandler.ServiceCallerAppRole;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static GeoFeature Feature(
        string id,
        string assetTypeId,
        GeoGeometry? geometry,
        string? name = null,
        IReadOnlyDictionary<string, string>? attributes = null,
        string? edgeTarget = null)
    {
        var feature = new GeoFeature
        {
            Id = id,
            Geometry = geometry,
            Properties = new GeoFeatureProperties { Name = name ?? "Feature " + id, AssetTypeId = assetTypeId },
        };
        foreach (var (key, value) in attributes ?? new Dictionary<string, string>(StringComparer.Ordinal))
        {
            feature.Properties.CustomAttributes[key] = value;
        }

        if (edgeTarget is not null)
        {
            feature.Topology.Add(new TopoEdge { TargetId = edgeTarget, Kind = "connected-to" });
        }

        return feature;
    }

    public static string NewId() => Guid.NewGuid().ToString();

    public static string Ndjson(IEnumerable<GeoFeature> features) =>
        string.Concat(features.Select(f => JsonSerializer.Serialize(f, GeoJsonSerializer.CompactOptions) + "\n"));

    public static string ChunkUrl(Guid organizationId, string importId, string? scope) =>
        $"/api/geoassets/organizations/{organizationId:D}/feature-imports/{importId}/chunks"
        + (scope is null ? string.Empty : "?scope=" + Uri.EscapeDataString(scope));

    public static string CompleteUrl(Guid organizationId, string importId) =>
        $"/api/geoassets/organizations/{organizationId:D}/feature-imports/{importId}/complete";

    public static Task<HttpResponseMessage> PostChunkAsync(
        FeatureImportHostContext host,
        Guid organizationId,
        string importId,
        string? scope,
        IEnumerable<GeoFeature> features,
        bool gzip = false,
        string? roles = ServiceCaller) =>
        PostChunkBodyAsync(host, organizationId, importId, scope, Ndjson(features), gzip, roles);

    public static async Task<HttpResponseMessage> PostChunkBodyAsync(
        FeatureImportHostContext host,
        Guid organizationId,
        string importId,
        string? scope,
        string body,
        bool gzip = false,
        string? roles = ServiceCaller)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(ChunkUrl(organizationId, importId, scope), UriKind.Relative));
        if (gzip)
        {
            using var buffer = new MemoryStream();
            await using (var compressor = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
            {
                await compressor.WriteAsync(Encoding.UTF8.GetBytes(body));
            }

            request.Content = new ByteArrayContent(buffer.ToArray());
            request.Content.Headers.ContentEncoding.Add("gzip");
        }
        else
        {
            request.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body));
        }

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/x-ndjson");
        if (roles is not null)
        {
            request.Headers.Add(HeaderRolesAuthenticationHandler.RolesHeader, roles);
        }

        return await host.Client.SendAsync(request);
    }

    public static async Task<HttpResponseMessage> PostCompleteAsync(
        FeatureImportHostContext host,
        Guid organizationId,
        string importId,
        string jsonBody,
        string? roles = ServiceCaller)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(CompleteUrl(organizationId, importId), UriKind.Relative))
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
        };
        if (roles is not null)
        {
            request.Headers.Add(HeaderRolesAuthenticationHandler.RolesHeader, roles);
        }

        return await host.Client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> CompleteAsync(
        FeatureImportHostContext host, Guid organizationId, string importId, string scope, string mode) =>
        PostCompleteAsync(host, organizationId, importId, JsonSerializer.Serialize(new { scope, mode }, Web));

    public static async Task<FeatureImportChunkResult> ReadChunkResultAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<FeatureImportChunkResult>(await response.Content.ReadAsStringAsync(), Web)!;

    public static async Task<FeatureImportCompletionResult> ReadCompletionAsync(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<FeatureImportCompletionResult>(await response.Content.ReadAsStringAsync(), Web)!;

    public static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString()
            : document.RootElement.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("code", out var nested) ? nested.GetString()
            : null;
    }
}
