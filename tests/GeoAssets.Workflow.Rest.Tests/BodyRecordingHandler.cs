using System.Net;
using System.Text;
using System.Text.Json;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// An <see cref="HttpMessageHandler"/> test double that keeps each request's method, path and
/// body (read while the request is still alive) and answers through a caller-supplied
/// responder. No network I/O.
/// </summary>
internal sealed class BodyRecordingHandler(Func<BodyRecordingHandler.Recorded, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Recorded> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
        var recorded = new Recorded(request.Method, request.RequestUri!.PathAndQuery, body);
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

    internal sealed record Recorded(HttpMethod Method, string PathAndQuery, string? Body)
    {
        public JsonElement Json => JsonDocument.Parse(Body!).RootElement.Clone();
    }
}
