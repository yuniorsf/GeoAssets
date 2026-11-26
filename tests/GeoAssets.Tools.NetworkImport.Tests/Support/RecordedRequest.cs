namespace GeoAssets.Tools.NetworkImport.Tests.Support;

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    IReadOnlyDictionary<string, string> Headers,
    string Body,
    bool WasGzip,
    string? ContentType);
