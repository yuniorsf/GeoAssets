using System.Text.Json;

namespace GeoAssets.NeutralOperator.Server.Tests.Support;

/// <summary>One request the host made to the stubbed kernel, read while the request was still alive.</summary>
internal sealed record RecordedRequest(
    HttpMethod Method,
    string Path,
    string Query,
    IReadOnlyDictionary<string, string> Headers,
    string? Body)
{
    public JsonElement Json => JsonDocument.Parse(Body ?? throw new InvalidOperationException("The request has no body.")).RootElement.Clone();

    public string? Authorization => Headers.GetValueOrDefault("Authorization");
}
