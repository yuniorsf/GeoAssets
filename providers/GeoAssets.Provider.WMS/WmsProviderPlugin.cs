using System.Text.RegularExpressions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Provider.WMS;

/// <summary>
/// Provider plugin that connects to the OGC WMS endpoint served by
/// <c>GeoAssets.Server</c> at <c>/api/geoassets/wms</c>.
///
/// Config fields shown in the boot / pool panel:
/// <list type="bullet">
///   <item><c>name</c>       — display label for the collection</item>
///   <item><c>url</c>        — GeoAssets REST API base URL (same as the REST plugin)</item>
///   <item><c>layerName</c>  — OGC LAYERS parameter (default: <c>geoassets:feature</c>)</item>
///   <item><c>tenantDomain</c> — optional: the distributor silo the gateway routes the tiles to, sent as their
///   <c>X-Tenant-Domain</c> header (e.g. <c>coelba.local</c>, XD01-473); a host name, or blank for none</item>
/// </list>
///
/// Because features are rendered server-side as PNG tiles, the connected provider
/// returns no features (<see cref="IAssetProvider.GetAll"/> is always empty).
/// The map's <c>WmsMapLayerRenderer</c> adds a Leaflet WMS tile layer for it instead of
/// iterating features (XD01-422).
/// </summary>
public sealed partial class WmsProviderPlugin : IProviderPlugin
{
    public string Id          => "wms";
    public string DisplayName => "WMS (OGC)";
    public string Description => "Render features as map image tiles via an OGC Web Map Service";

    public IReadOnlyList<ProviderConfigField> ConfigFields =>
    [
        new("name",
            Label:       "Layer name",
            Placeholder: "WMS layer"),
        new("url",
            Label:       "GeoAssets REST API URL",
            Type:        ProviderFieldType.Url,
            Placeholder: "https://my-server.com/api/geoassets",
            Required:    true),
        new("layerName",
            Label:        "OGC layer name",
            Placeholder:  "geoassets:feature",
            DefaultValue: "geoassets:feature"),
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
        var restBase = config.Get("url").Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(restBase))
            throw new InvalidOperationException("GeoAssets REST API URL is required.");

        // Derive the WMS endpoint from the REST base URL (same CORS-configured prefix).
        var wmsUrl    = restBase.EndsWith("/wms", StringComparison.OrdinalIgnoreCase)
            ? restBase
            : restBase + "/wms";
        var layerName = config.Get("layerName", "geoassets:feature").Trim();
        if (string.IsNullOrEmpty(layerName)) layerName = "geoassets:feature";

        var tenantDomain = config.Get("tenantDomain").Trim();
        if (tenantDomain.Length > 0 && !TenantDomainPattern().IsMatch(tenantDomain))
            throw new InvalidOperationException($"Tenant domain '{tenantDomain}' is not a host name (e.g. coelba.local).");

        IAssetProvider provider = new WmsAssetProvider(wmsUrl, layerName, tenantDomain: tenantDomain.Length > 0 ? tenantDomain : null);
        return Task.FromResult(provider);
    }

    // A DNS host name (labels of letters, digits and inner hyphens, dot-separated): it travels as a request header.
    [GeneratedRegex(@"^(?=.{1,253}$)[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TenantDomainPattern();
}
