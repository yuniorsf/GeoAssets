using Yarp.ReverseProxy.Forwarder;

namespace GeoAssets.Gateway.Tests.EndToEnd;

/// <summary>
/// The gateway's outgoing connections, in process: the forwarder of YARP sends each proxied request to the handler registered for
/// the destination's authority (<c>kernel.test</c>, <c>neutral-operator.test</c>), so the gateway's own <c>Program</c> runs unchanged
/// in front of hosts that listen on no socket.
/// </summary>
internal sealed class HostRoutingForwarder(IReadOnlyDictionary<string, HttpMessageHandler> hosts) : IForwarderHttpClientFactory
{
    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(new Router(hosts), disposeHandler: false);

    private sealed class Router(IReadOnlyDictionary<string, HttpMessageHandler> hosts) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var authority = request.RequestUri!.Authority;
            if (!hosts.TryGetValue(authority, out var handler))
                throw new HttpRequestException($"The gateway forwarded to '{authority}', which is no destination of this test.");

            return new HttpMessageInvoker(handler, disposeHandler: false).SendAsync(request, cancellationToken);
        }
    }
}
