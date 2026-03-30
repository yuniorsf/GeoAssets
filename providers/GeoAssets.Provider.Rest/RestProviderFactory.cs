using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.Http;

namespace GeoAssets.Provider.Rest;

/// <summary>
/// <see cref="IAsyncProviderFactory"/> that creates a <see cref="RestAssetProvider"/>
/// connecting to a remote GeoAssets REST API.
///
/// The connection string must be the API base URL, e.g.
/// <c>http://localhost:5000/api/geoassets</c>.
/// The factory loads the full dataset from the server on creation.
/// </summary>
public sealed class RestProviderFactory : IAsyncProviderFactory
{
    private readonly IHttpClientFactory _httpFactory;

    public RestProviderFactory(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    public string ProviderName => "REST";

    /// <summary>Creates a provider without loading initial data. Prefer <see cref="CreateAsync"/>.</summary>
    public IAssetProvider Create(string connectionString)
    {
        var client = BuildClient(connectionString);
        return new RestAssetProvider(client);
    }

    /// <summary>Creates a provider and fetches the full dataset from the API.</summary>
    public async Task<IAssetProvider> CreateAsync(string connectionString, CancellationToken ct = default)
    {
        var client   = BuildClient(connectionString);
        var provider = new RestAssetProvider(client);
        await provider.InitializeAsync(ct);
        return provider;
    }

    private HttpClient BuildClient(string baseUrl)
    {
        var client = _httpFactory.CreateClient();
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        return client;
    }
}
