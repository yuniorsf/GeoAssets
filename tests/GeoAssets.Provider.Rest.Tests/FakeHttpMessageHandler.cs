namespace GeoAssets.Provider.Rest.Tests;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _respond;

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
    public FakeHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) =>
        _respond = respond;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return await _respond(request);
    }
}
