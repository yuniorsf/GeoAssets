namespace GeoAssets.Gateway.Tests;

/// <summary>A request as a backend received it.</summary>
internal sealed record SeenRequest(string Method, string Path, string Query, IReadOnlyDictionary<string, string> Headers, string Body)
{
    public string? Header(string name) => Headers.GetValueOrDefault(name);
}
