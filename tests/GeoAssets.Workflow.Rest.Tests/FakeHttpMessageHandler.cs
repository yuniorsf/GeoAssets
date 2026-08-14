using System.Net.Http;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// Minimal <see cref="HttpMessageHandler"/> test double — routes every request through a
/// caller-supplied responder and records requests for assertions, without any real network I/O.
/// </summary>
internal sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }
}
