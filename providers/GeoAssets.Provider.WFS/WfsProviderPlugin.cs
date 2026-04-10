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
///   <item><c>url</c>  — WFS service base URL (required)</item>
///   <item><c>typeName</c> — feature type to query (default: <c>geoassets:feature</c>)</item>
///   <item><c>maxFeatures</c> — upper bound on features loaded per request</item>
/// </list>
/// </summary>
public sealed class WfsProviderPlugin(WfsProviderFactory factory) : IProviderPlugin
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
            Label:       "WFS endpoint URL",
            Type:        ProviderFieldType.Url,
            Placeholder: "https://my-server.com/wfs",
            Required:    true),
        new("typeName",
            Label:        "Feature type name",
            Placeholder:  "geoassets:feature",
            DefaultValue: "geoassets:feature"),
        new("maxFeatures",
            Label:        "Max features per request",
            Type:         ProviderFieldType.Number,
            DefaultValue: "10000")
    ];

    public async Task<IAssetProvider> CreateAsync(
        ProviderConfig    config,
        IServiceProvider  services,
        CancellationToken ct = default)
    {
        var url = config.Get("url").Trim();
        if (string.IsNullOrEmpty(url))
            throw new InvalidOperationException("WFS endpoint URL is required.");

        var typeName    = config.Get("typeName", "geoassets:feature").Trim();
        var maxFeatures = int.TryParse(config.Get("maxFeatures", "10000"), out var n) ? n : 10_000;

        return await factory.CreateAsync(url, typeName, maxFeatures, ct);
    }
}
