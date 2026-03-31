using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Provider.Rest;

/// <summary>
/// Plugin that connects to a remote GeoAssets REST API.
/// </summary>
public sealed class RestProviderPlugin(RestProviderFactory factory) : IProviderPlugin
{
    public string Id => "rest";
    public string DisplayName => "REST API";
    public string Description => "Connect to a remote GeoAssets server via HTTP";

    public IReadOnlyList<ProviderConfigField> ConfigFields =>
    [
        new("name",
            Label: "Collection name",
            Placeholder: "Remote collection"),
        new("url",
            Label: "API base URL",
            Type: ProviderFieldType.Url,
            Placeholder: "https://api.example.com/api/geoassets",
            Required: true)
    ];

    public async Task<IAssetProvider> CreateAsync(
        ProviderConfig config,
        IServiceProvider services,
        CancellationToken ct = default)
    {
        var url = config.Get("url").Trim();
        if (string.IsNullOrEmpty(url))
            throw new InvalidOperationException("API base URL is required.");

        return await factory.CreateAsync(url, ct);
    }
}
