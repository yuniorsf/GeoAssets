namespace GeoAssets.Provider.Rest.Tests;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
    private readonly Lock _requestsGate = new();

    /// <summary>
    /// Every request sent, in arrival order. Writes are synchronized because concurrent dispatch
    /// (the tiled fetch's throttle tests) calls <see cref="SendAsync"/> from several threads at once;
    /// read it only after the requests under test have completed.
    /// </summary>
    public List<HttpRequestMessage> Requests { get; } = [];

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : this(request => Task.FromResult(respond(request)))
    {
    }

    /// <summary>
    /// Genuinely-async variant — unlike the synchronous overload (whose result is computed eagerly,
    /// before <c>SendAsync</c> returns), this one lets <paramref name="respond"/> <c>await</c>
    /// something without blocking the calling thread. Needed for tests that dispatch several
    /// concurrent requests and control their completion order (e.g. XD01-172's tiled fetch) —
    /// a synchronous, blocking <c>respond</c> would serialize dispatch itself.
    /// </summary>
    public FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : this((request, _) => respond(request))
    {
    }

    /// <summary>
    /// Variant that sees the request's <see cref="CancellationToken"/> — for tests proving a superseded fetch is
    /// aborted mid-flight (XD01-429).
    /// </summary>
    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) =>
        _respond = respond;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        lock (_requestsGate)
            Requests.Add(request);
        return _respond(request, ct);
    }
}
