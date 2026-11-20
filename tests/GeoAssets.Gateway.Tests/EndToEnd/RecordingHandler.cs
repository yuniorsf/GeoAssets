namespace GeoAssets.Gateway.Tests.EndToEnd;

/// <summary>Keeps every request that passes through to <paramref name="inner"/>: method, path and query, as the next hop sees them.</summary>
internal sealed class RecordingHandler(HttpMessageHandler inner) : HttpMessageHandler
{
    private readonly Lock _gate = new();
    private readonly List<(HttpMethod Method, string Path, string Query)> _requests = [];

    public IReadOnlyList<(HttpMethod Method, string Path, string Query)> Requests
    {
        get
        {
            lock (_gate) return [.. _requests];
        }
    }

    public IReadOnlyList<string> Paths => [.. Requests.Select(r => r.Path)];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (_gate) _requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query));
        return new HttpMessageInvoker(inner, disposeHandler: false).SendAsync(request, cancellationToken);
    }
}
