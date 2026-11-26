using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.Tools.NetworkImport.Tests.Support;

internal sealed class FakeKernelHandler : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];
    private int _chunkRequests;

    public IReadOnlyList<RecordedRequest> Requests => _requests;

    public List<AssetType> ExistingAssetTypes { get; } = [];

    public Func<int, HttpStatusCode>? ChunkStatus { get; set; }

    public HttpStatusCode AssetTypeListStatus { get; set; } = HttpStatusCode.OK;

    public Func<string, GeoFeature?>? FeatureLookup { get; set; }

    public IEnumerable<RecordedRequest> Chunks =>
        _requests.Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("/chunks", StringComparison.Ordinal));

    public IEnumerable<RecordedRequest> Completions =>
        _requests.Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("/complete", StringComparison.Ordinal));

    public IEnumerable<RecordedRequest> AssetTypePosts =>
        _requests.Where(r => r.Method == HttpMethod.Post && r.Uri.AbsolutePath.EndsWith("/asset-types", StringComparison.Ordinal));

    public IEnumerable<RecordedRequest> FeatureGets =>
        _requests.Where(r => r.Method == HttpMethod.Get && r.Uri.AbsolutePath.Contains("/features/", StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = string.Empty;
        var gzip = false;
        string? contentType = null;
        if (request.Content is not null)
        {
            contentType = request.Content.Headers.ContentType?.MediaType;
            gzip = request.Content.Headers.ContentEncoding.Contains("gzip", StringComparer.OrdinalIgnoreCase);
            var bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            if (gzip)
            {
                using var source = new MemoryStream(bytes);
                using var gz = new GZipStream(source, CompressionMode.Decompress);
                using var reader = new StreamReader(gz, Encoding.UTF8);
                body = await reader.ReadToEndAsync(cancellationToken);
            }
            else
            {
                body = Encoding.UTF8.GetString(bytes);
            }
        }

        var uri = request.RequestUri!;
        var recorded = new RecordedRequest(request.Method, uri, headers, body, gzip, contentType);
        _requests.Add(recorded);
        return Respond(recorded);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private HttpResponseMessage Respond(RecordedRequest request)
    {
        var path = request.Uri.AbsolutePath;
        if (request.Method == HttpMethod.Get && path.EndsWith("/asset-types", StringComparison.Ordinal))
        {
            return Json(AssetTypeListStatus, JsonSerializer.Serialize(ExistingAssetTypes, GeoJsonSerializer.Options));
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/asset-types", StringComparison.Ordinal))
        {
            return new HttpResponseMessage(HttpStatusCode.Created);
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/chunks", StringComparison.Ordinal))
        {
            _chunkRequests++;
            var status = ChunkStatus?.Invoke(_chunkRequests) ?? HttpStatusCode.OK;
            var count = request.Body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            return status == HttpStatusCode.OK
                ? Json(status, $"{{\"inserted\":{count},\"updated\":0,\"rejected\":[]}}")
                : Json(status, "{\"title\":\"failure\"}");
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/complete", StringComparison.Ordinal))
        {
            return Json(HttpStatusCode.OK, "{\"removed\":0}");
        }

        if (request.Method == HttpMethod.Get && path.Contains("/features/", StringComparison.Ordinal))
        {
            var id = path[(path.LastIndexOf('/') + 1)..];
            var feature = FeatureLookup?.Invoke(id);
            return feature is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(HttpStatusCode.OK, JsonSerializer.Serialize(feature, GeoJsonSerializer.Options));
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
