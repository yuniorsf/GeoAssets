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
/// </list>
///
/// Because features are rendered server-side as PNG tiles, the connected provider
/// returns no features (<see cref="IAssetProvider.GetAll"/> is always empty).
/// The pool panel detects <see cref="IWmsProvider"/> and adds a Leaflet WMS tile layer
/// instead of iterating features.
/// </summary>
public sealed class WmsProviderPlugin : IProviderPlugin
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
            DefaultValue: "geoassets:feature")
    ];

    public Task<IAssetProvider> CreateAsync(
        ProviderConfig    config,
        IServiceProvider  services,
        CancellationToken ct = default)
    {
        var restBase = config.Get("url").Trim().TrimEnd('/');
        if (string.IsNullOrEmpty(restBase))
            throw new InvalidOperationException("GeoAssets REST API URL is required.");

        // Derive the WMS endpoint from the REST base URL (same CORS-configured prefix).
        var wmsUrl    = restBase.EndsWith("/wms", StringComparison.OrdinalIgnoreCase)
            ? restBase
            : restBase + "/wms";
        var layerName = config.Get("layerName", "geoassets:feature").Trim();
        if (string.IsNullOrEmpty(layerName)) layerName = "geoassets:feature";

        IAssetProvider provider = new WmsAssetProvider(wmsUrl, layerName);
        return Task.FromResult(provider);
    }
}
