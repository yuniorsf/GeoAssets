using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>Request and assertion helpers shared by the order-target endpoint tests (XD01-308).</summary>
internal static class OrderTargetsApiAssertions
{
    public static Uri Path(string relative) => new(relative, UriKind.Relative);

    public static Task<HttpResponseMessage> PostJsonAsync(this HttpClient client, string path, object body) =>
        client.PostAsJsonAsync(Path(path), body);

    public static Task<HttpResponseMessage> PostRawAsync(this HttpClient client, string path, string body) =>
        client.PostAsync(Path(path), new StringContent(body, Encoding.UTF8, "application/json"));

    public static Task<HttpResponseMessage> PatchRawAsync(this HttpClient client, string path, string body) =>
        client.PatchAsync(Path(path), new StringContent(body, Encoding.UTF8, "application/json"));

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(expected, "the body was: {0}", text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }

    /// <summary>
    /// Asserts the response is an <c>application/problem+json</c> body with the given status and
    /// stable <c>code</c>, and returns the body for the extension members.
    /// </summary>
    public static async Task<JsonElement> ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, "the body was: {0}", text);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = JsonDocument.Parse(text).RootElement.Clone();
        body.GetProperty("code").GetString().Should().Be(code);
        body.GetProperty("status").GetInt32().Should().Be((int)status);
        body.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("type").GetString().Should().Be($"urn:geoassets:problem:{code}");
        return body;
    }

    public static string[] Strings(this JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    public static object FenceBody(object? rings = null, int? page = null, int? pageSize = null, bool? acceptTruncation = null) => new
    {
        strategyId = "polygon-fence",
        parameters = new { rings = rings ?? OrderTargetsApiHost.FenceRings() },
        page = page ?? 1,
        pageSize = pageSize ?? 50,
        acceptTruncation = acceptTruncation ?? false,
    };
}
