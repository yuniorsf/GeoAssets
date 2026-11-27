using System.Globalization;
using System.Text.RegularExpressions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Provider.WFS;

/// <summary>
/// Provider plugin that connects to any OGC WFS 2.0 server — including the
/// WFS endpoint exposed by <c>GeoAssets.Server</c> at <c>/wfs</c>.
///
/// Config fields shown in the boot dialog / pool panel:
/// <list type="bullet">
///   <item><c>name</c> — display label for the collection in the pool panel</item>
///   <item><c>url</c>  — the GeoAssets server's base URL, <c>/wfs</c> appended — in the browser the gateway's, the
///   only origin that answers CORS — or another WFS service's full <c>…/wfs</c> URL (required)</item>
///   <item><c>typeName</c> — feature type to query (default: <c>geoassets:feature</c>)</item>
///   <item><c>maxFeatures</c> — upper bound on features loaded per request</item>
///   <item><c>tenantDomain</c> — optional: the distributor silo the gateway routes the requests to, sent as their
///   <c>X-Tenant-Domain</c> header (e.g. <c>coelba.local</c>, XD01-477); a host name, or blank for none</item>
/// </list>
/// Requests go through <see cref="WfsProviderFactory.HttpClientName"/>, so a GeoAssets URL carries the signed-in user's
/// token where the host attaches one, and any other WFS server none.
/// </summary>
public sealed partial class WfsProviderPlugin(WfsProviderFactory factory) : IProviderPlugin
{
    public string Id          => "wfs";
    public string DisplayName => "WFS (OGC)";
    public string Description => "Connect to an OGC Web Feature Service 2.0 with PostGIS back-end";

    public IReadOnlyList<ProviderConfigField> ConfigFields =>
    [
        new("name",
            Label:       "Collection name",
            Placeholder: "WFS layer"),
        new("url",
            Label:       "Server URL — in the browser, the GeoAssets gateway (/wfs is appended)",
            Type:        ProviderFieldType.Url,
            Placeholder: "https://my-gateway.com",
            Required:    true),
        new("typeName",
            Label:        "Feature type name",
            Placeholder:  "geoassets:feature",
            DefaultValue: "geoassets:feature"),
        new("maxFeatures",
            Label:        "Max features per request",
            Type:         ProviderFieldType.Number,
            DefaultValue: "10000"),
        new("tenantDomain",
            Label:       "Tenant domain (silo, optional)",
            Placeholder: "coelba.local")
    ];

    public Task<IAssetProvider> CreateAsync(
        ProviderConfig    config,
        IServiceProvider  services,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        var url = config.Get("url").Trim();
        if (string.IsNullOrEmpty(url))
            throw new InvalidOperationException("WFS endpoint URL is required.");

        var typeName    = config.Get("typeName", "geoassets:feature").Trim();
        var maxFeatures = int.TryParse(config.Get("maxFeatures", "10000"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 10_000;

        var tenantDomain = config.Get("tenantDomain").Trim();
        if (tenantDomain.Length > 0 && !TenantDomainPattern().IsMatch(tenantDomain))
            throw new InvalidOperationException($"Tenant domain '{tenantDomain}' is not a host name (e.g. coelba.local).");

        return factory.CreateAsync(url, typeName, maxFeatures, tenantDomain.Length > 0 ? tenantDomain : null, ct);
    }

    // A DNS host name (labels of letters, digits and inner hyphens, dot-separated): it travels as a request header.
    [GeneratedRegex(@"^(?=.{1,253}$)[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TenantDomainPattern();
}
