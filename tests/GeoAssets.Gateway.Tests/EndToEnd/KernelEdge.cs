using System.Net;
using System.Text;

namespace GeoAssets.Gateway.Tests.EndToEnd;

/// <summary>
/// The kernel as the gateway sees it: it answers the order-type registry (<c>GET /api/registry/order-types</c>, with
/// <paramref name="descriptorsJson"/>) and every other request with <paramref name="rest"/> — nothing (404) while the kernel does not host
/// the vertical, the workflow API of the host standing in for the kernel-hosted module in the rollback.
/// </summary>
internal sealed class KernelEdge(string descriptorsJson, HttpMessageHandler? rest = null) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Get && string.Equals(request.RequestUri!.AbsolutePath, "/api/registry/order-types", StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(descriptorsJson, Encoding.UTF8, "application/json") });
        }

        return rest is null
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
            : new HttpMessageInvoker(rest, disposeHandler: false).SendAsync(request, cancellationToken);
    }
}
