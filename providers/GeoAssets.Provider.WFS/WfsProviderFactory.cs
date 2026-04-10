using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Provider.WFS;

/// <summary>
/// Creates <see cref="WfsAssetProvider"/> instances that connect to an
/// OGC WFS 2.0 endpoint.
///
/// The connection string is interpreted as:
/// <c>{baseUrl}|{typeName}|{maxFeatures}</c>
/// where only <c>baseUrl</c> is required.
///
/// Examples:
/// <code>
/// https://my-server.com/wfs
/// https://my-server.com/wfs|geoassets:feature|5000
/// </code>
/// </summary>
public sealed class WfsProviderFactory : IAsyncProviderFactory
{
    private const string DefaultTypeName   = "geoassets:feature";
    private const int    DefaultMaxFeatures = 10_000;

    private readonly IHttpClientFactory           _httpFactory;
    private readonly ILogger<WfsAssetProvider>    _providerLogger;

    public WfsProviderFactory(
        IHttpClientFactory        httpFactory,
        ILogger<WfsAssetProvider> providerLogger)
    {
        _httpFactory    = httpFactory;
        _providerLogger = providerLogger;
    }

    public string ProviderName => "WFS";

    /// <summary>Creates a provider without loading initial data. Prefer <see cref="CreateAsync"/>.</summary>
    public IAssetProvider Create(string connectionString)
    {
        var (baseUrl, typeName, maxFeatures) = ParseConnectionString(connectionString);
        var wfs      = new WfsClient(BuildHttpClient(baseUrl));
        return new WfsAssetProvider(wfs, typeName, maxFeatures, _providerLogger);
    }

    /// <summary>Creates a provider and fetches the initial feature set from the WFS service.</summary>
    public async Task<IAssetProvider> CreateAsync(
        string            connectionString,
        CancellationToken ct = default)
    {
        var (baseUrl, typeName, maxFeatures) = ParseConnectionString(connectionString);
        var wfs      = new WfsClient(BuildHttpClient(baseUrl));
        var provider = new WfsAssetProvider(wfs, typeName, maxFeatures, _providerLogger);
        await provider.InitializeAsync(ct);
        return provider;
    }

    /// <summary>
    /// Convenience overload used by <see cref="WfsProviderPlugin"/>.
    /// Accepts the three config values directly instead of a combined connection string.
    /// </summary>
    internal async Task<IAssetProvider> CreateAsync(
        string            baseUrl,
        string            typeName,
        int               maxFeatures,
        CancellationToken ct = default)
    {
        var wfs      = new WfsClient(BuildHttpClient(baseUrl));
        var provider = new WfsAssetProvider(wfs, typeName, maxFeatures, _providerLogger);
        await provider.InitializeAsync(ct);
        return provider;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static (string baseUrl, string typeName, int maxFeatures)
        ParseConnectionString(string connectionString)
    {
        var parts = connectionString.Split('|');
        var baseUrl     = parts[0].Trim();
        var typeName    = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])
            ? parts[1].Trim()
            : DefaultTypeName;
        var maxFeatures = parts.Length > 2 && int.TryParse(parts[2], out var n)
            ? n
            : DefaultMaxFeatures;
        return (baseUrl, typeName, maxFeatures);
    }

    private HttpClient BuildHttpClient(string baseUrl)
    {
        var client = _httpFactory.CreateClient();
        // The user provides the REST API base URL (e.g. https://server/api/geoassets).
        // WfsClient uses relative URLs like "?SERVICE=WFS...", so BaseAddress must end
        // at the WFS path so they resolve to …/api/geoassets/wfs?SERVICE=WFS…
        var trimmed = baseUrl.TrimEnd('/');
        var wfsBase = trimmed.EndsWith("/wfs", StringComparison.OrdinalIgnoreCase)
            ? trimmed           // user already gave the full WFS path
            : trimmed + "/wfs"; // derive from REST base
        client.BaseAddress = new Uri(wfsBase + "/");
        return client;
    }
}
