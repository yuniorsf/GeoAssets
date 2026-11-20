namespace GeoAssets.Server;

/// <summary>
/// The token audiences the kernel accepts (XD01-390, CIAM model (b): one app registration per API). The vertical hosts
/// (GeoAssets.IpBilling.Server, GeoAssets.NeutralOperator.Server) validate the caller's token against their own API
/// registration and forward that same token to the kernel's caller-scoped identity routes, so the kernel must accept their
/// audiences too: <c>AzureAdCiam:AdditionalAudiences</c> lists them. With the list empty or absent the kernel keeps
/// Microsoft.Identity.Web's own audience validation (its <c>ClientId</c> for v2.0 tokens, <c>api://{ClientId}</c> for
/// v1.0 tokens); with it set, a token is accepted whose <c>aud</c> is the kernel's <c>ClientId</c>,
/// <c>api://{ClientId}</c>, or any listed value. Only the audience set widens: issuer, signature and lifetime validation
/// are unchanged.
/// </summary>
public static class KernelAudiences
{
    /// <summary>The key, under the kernel's <c>AzureAdCiam</c> section, of the extra accepted audiences (a string array).</summary>
    public const string AdditionalAudiencesKey = "AdditionalAudiences";

    /// <summary>
    /// The accepted audiences: <paramref name="clientId"/> and <c>api://{clientId}</c> (when <paramref name="clientId"/> is
    /// not blank), then each of <paramref name="additional"/> — trimmed, blanks dropped, distinct (ordinal), first
    /// occurrence first.
    /// </summary>
    public static IReadOnlyList<string> Resolve(string? clientId, IEnumerable<string?>? additional)
    {
        var audiences = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string? audience)
        {
            var value = audience?.Trim();
            if (!string.IsNullOrEmpty(value) && seen.Add(value)) audiences.Add(value);
        }

        if (!string.IsNullOrWhiteSpace(clientId))
        {
            Add(clientId);
            Add("api://" + clientId.Trim());
        }

        foreach (var audience in additional ?? [])
            Add(audience);

        return audiences;
    }

    /// <summary>True when <paramref name="additional"/> names at least one audience — the only case the kernel widens its audience set.</summary>
    public static bool AnyAdditional(IEnumerable<string?>? additional) =>
        additional?.Any(a => !string.IsNullOrWhiteSpace(a)) ?? false;
}
