using System.Net;
using System.Text;
using System.Text.Json;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// The kernel's root as a vertical host's clients see it: records each request's method, path and query, headers and body (read while the
/// request is still alive) and answers through a caller-supplied responder — or fails the connection when the responder throws.
/// </summary>
internal sealed class RecordingKernelHandler(Func<RecordingKernelHandler.Recorded, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Recorded> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers
            .Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(h => h.Key, h => string.Join(',', h.Value), StringComparer.OrdinalIgnoreCase);
        var recorded = new Recorded(request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query, request.RequestUri.OriginalString, headers, body);
        Requests.Add(recorded);
        return respond(recorded);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Problem(HttpStatusCode status, string code, string extraMembers = "") =>
        new(status)
        {
            Content = new StringContent(
                $$"""{ "type": "urn:geoassets:problem:{{code}}", "title": "Title of {{code}}", "status": {{(int)status}}, "code": "{{code}}", "detail": "Detail of {{code}}"{{(extraMembers.Length == 0 ? "" : ", " + extraMembers)}} }""",
                Encoding.UTF8,
                "application/problem+json"),
        };

    internal sealed record Recorded(
        HttpMethod Method, string Path, string Query, string Original, IReadOnlyDictionary<string, string> Headers, string? Body)
    {
        public JsonElement Json => JsonDocument.Parse(Body!).RootElement.Clone();

        public string? Authorization => Headers.GetValueOrDefault("Authorization");
    }
}
