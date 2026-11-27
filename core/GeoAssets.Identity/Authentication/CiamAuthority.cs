using System.Diagnostics.CodeAnalysis;

namespace GeoAssets.Identity.Authentication;

/// <summary>
/// The token authority of the GeoAssets Entra CIAM tenant: <c>{instance}{tenant id}</c>, e.g.
/// <c>https://geoassets.ciamlogin.com/&lt;tenant id&gt;</c> (XD01-452). The kernel accepts only the issuer of its CIAM
/// instance (<c>https://&lt;tenant id&gt;.ciamlogin.com/&lt;tenant id&gt;/v2.0</c>), so a client-credentials token must be asked
/// from this authority. MSAL's <c>WithTenantId</c> asks <c>login.microsoftonline.com</c> instead, whose tokens carry that
/// host's issuer and are refused with 401 (<c>IDX40001</c>).
/// </summary>
public static class CiamAuthority
{
    /// <summary>
    /// Builds the authority from a CIAM instance (an absolute https URL, such as <c>AzureAdCiam:Instance</c>; only its
    /// scheme, host and port are used) and a tenant id. Returns <see langword="false"/> when the instance is missing, not
    /// absolute or not https, or the tenant id is blank.
    /// </summary>
    public static bool TryCreate(string? instance, string? tenantId, [NotNullWhen(true)] out Uri? authority)
    {
        authority = null;
        if (string.IsNullOrWhiteSpace(instance) || string.IsNullOrWhiteSpace(tenantId))
            return false;
        if (!Uri.TryCreate(instance.Trim(), UriKind.Absolute, out var root)
            || !string.Equals(root.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
            return false;

        authority = new Uri($"{root.GetLeftPart(UriPartial.Authority)}/{Uri.EscapeDataString(tenantId.Trim())}", UriKind.Absolute);
        return true;
    }
}
