using System.Net;
using System.Net.Http.Headers;
using Microsoft.Net.Http.Headers;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// Sends the current request's own bearer token on to the kernel's caller-scoped identity routes (XD01-382): the kernel
/// validates it and answers for that caller — who it is, its roles, permissions and its organization's grants. Nothing
/// the caller states besides its token is sent, and nothing is sent outside a request.
/// <para>
/// A kernel 401 to a request that carried the forwarded token (XD01-390) is the kernel refusing a token this host
/// accepted: it throws <see cref="KernelRejectedCallerTokenException"/> with the kernel's <c>WWW-Authenticate</c>
/// <c>error</c>/<c>error_description</c> and the <c>aud</c>/<c>iss</c> claims of the request's authenticated principal
/// (never the token). Any other answer — a 401 when no token was forwarded included — is returned as it is.
/// </para>
/// </summary>
public sealed class ForwardedBearerTokenHandler(IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = httpContextAccessor.HttpContext;
        var authorization = context?.Request.Headers[HeaderNames.Authorization].ToString();
        var forwarded = false;
        if (!string.IsNullOrWhiteSpace(authorization) && AuthenticationHeaderValue.TryParse(authorization, out var header)
            && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = header;
            forwarded = true;
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!forwarded || response.StatusCode != HttpStatusCode.Unauthorized) return response;

        var (error, errorDescription) = BearerChallenge(response.Headers.WwwAuthenticate);
        response.Dispose();

        var user = context?.User;
        var audiences = user?.FindAll("aud").Select(c => c.Value).Where(v => !string.IsNullOrWhiteSpace(v)).ToList() ?? [];
        throw new KernelRejectedCallerTokenException(
            request.RequestUri?.AbsolutePath,
            error,
            errorDescription,
            audiences.Count == 0 ? null : string.Join(", ", audiences),
            user?.FindFirst("iss")?.Value);
    }

    /// <summary>The <c>error</c> and <c>error_description</c> parameters of the kernel's Bearer challenge, when present.</summary>
    private static (string? Error, string? ErrorDescription) BearerChallenge(HttpHeaderValueCollection<AuthenticationHeaderValue> challenges)
    {
        string? error = null;
        string? errorDescription = null;
        foreach (var challenge in challenges)
        {
            if (!string.Equals(challenge.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(challenge.Parameter))
                continue;

            foreach (var (name, value) in ChallengeParameters(challenge.Parameter))
            {
                if (string.Equals(name, "error", StringComparison.OrdinalIgnoreCase)) error ??= value;
                else if (string.Equals(name, "error_description", StringComparison.OrdinalIgnoreCase)) errorDescription ??= value;
            }
        }

        return (error, errorDescription);
    }

    /// <summary>The <c>name=value</c> / <c>name="value"</c> pairs of a challenge's parameter list (RFC 9110 §11.2).</summary>
    private static IEnumerable<(string Name, string Value)> ChallengeParameters(string parameters)
    {
        var i = 0;
        while (i < parameters.Length)
        {
            while (i < parameters.Length && (parameters[i] == ',' || char.IsWhiteSpace(parameters[i]))) i++;
            var nameStart = i;
            while (i < parameters.Length && parameters[i] != '=' && parameters[i] != ',') i++;
            var name = parameters[nameStart..i].Trim();
            if (i >= parameters.Length || parameters[i] != '=') continue;
            i++;
            while (i < parameters.Length && char.IsWhiteSpace(parameters[i])) i++;

            var value = new System.Text.StringBuilder();
            if (i < parameters.Length && parameters[i] == '"')
            {
                i++;
                while (i < parameters.Length && parameters[i] != '"')
                {
                    if (parameters[i] == '\\' && i + 1 < parameters.Length) i++;
                    value.Append(parameters[i++]);
                }

                i++;
            }
            else
            {
                while (i < parameters.Length && parameters[i] != ',') value.Append(parameters[i++]);
            }

            if (name.Length > 0) yield return (name, value.ToString().Trim());
        }
    }
}
