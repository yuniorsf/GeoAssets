using System.Net.Http.Headers;
using Microsoft.Net.Http.Headers;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// Sends the current request's own bearer token on to the kernel's caller-scoped identity routes (XD01-382): the kernel
/// validates it and answers for that caller — who it is, its roles, permissions and its organization's grants. Nothing
/// the caller states besides its token is sent, and nothing is sent outside a request.
/// </summary>
internal sealed class ForwardedBearerTokenHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var authorization = httpContextAccessor.HttpContext?.Request.Headers[HeaderNames.Authorization].ToString();
        if (!string.IsNullOrWhiteSpace(authorization) && AuthenticationHeaderValue.TryParse(authorization, out var header)
            && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = header;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
