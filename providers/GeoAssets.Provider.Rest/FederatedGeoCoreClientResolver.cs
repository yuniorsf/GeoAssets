using System.Diagnostics.CodeAnalysis;
using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace GeoAssets.Provider.Rest;

/// <summary>
/// Fail-closed multi-shard <see cref="IGeoCoreClientResolver"/> backed by <see cref="IHttpClientFactory"/>.
/// </summary>
public sealed class FederatedGeoCoreClientResolver : IGeoCoreClientResolver
{
    private readonly Dictionary<string, IGeoCoreClient> _clientsByCatalog;
    private readonly Dictionary<Guid, string> _catalogByOrg;
    private readonly string? _defaultCatalogId;

    public FederatedGeoCoreClientResolver(
        IHttpClientFactory httpClientFactory,
        IOptions<GeoCoreFederationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);

        var config = options.Value
            ?? throw new InvalidOperationException("GeoCoreFederationOptions value cannot be null.");

        _clientsByCatalog = new Dictionary<string, IGeoCoreClient>(StringComparer.Ordinal);
        _catalogByOrg = [];

        foreach (var (key, endpoint) in config.Catalogs)
        {
            RegisterCatalogEndpoint(httpClientFactory, key, endpoint);
        }

        if (config.DefaultCatalogInstanceId is not null)
        {
            if (string.IsNullOrWhiteSpace(config.DefaultCatalogInstanceId) ||
                !_clientsByCatalog.ContainsKey(config.DefaultCatalogInstanceId))
            {
                throw new InvalidOperationException(
                    $"DefaultCatalogInstanceId '{config.DefaultCatalogInstanceId}' does not match any registered catalog shard.");
            }

            _defaultCatalogId = config.DefaultCatalogInstanceId;
        }
    }

    private void RegisterCatalogEndpoint(
        IHttpClientFactory httpClientFactory,
        string key,
        GeoCoreCatalogEndpoint? endpoint)
    {
        if (endpoint is null)
        {
            throw new InvalidOperationException(
                $"Catalog endpoint configuration for key '{key}' cannot be null.");
        }

        var catalogId = !string.IsNullOrWhiteSpace(endpoint.CatalogInstanceId)
            ? endpoint.CatalogInstanceId
            : key;

        if (string.IsNullOrWhiteSpace(catalogId))
        {
            throw new InvalidOperationException("Catalog instance ID cannot be empty or whitespace.");
        }

        if (string.IsNullOrWhiteSpace(endpoint.BaseUrl) ||
            !Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out var baseUri) ||
            (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Catalog '{catalogId}' has an invalid or non-HTTP(S) absolute BaseUrl '{endpoint.BaseUrl}'.");
        }

        if (_clientsByCatalog.ContainsKey(catalogId))
        {
            throw new InvalidOperationException(
                $"Duplicate catalog instance ID '{catalogId}' in GeoCoreFederation configuration.");
        }

        foreach (var orgId in endpoint.OrganizationIds)
        {
            if (orgId == Guid.Empty) continue;
            if (!_catalogByOrg.TryAdd(orgId, catalogId))
            {
                throw new InvalidOperationException(
                    $"Organization '{orgId}' is claimed by multiple catalog shards ('{_catalogByOrg[orgId]}' and '{catalogId}').");
            }
        }

        var httpClient = httpClientFactory.CreateClient($"GeoAssetsServer:{catalogId}");
        httpClient.BaseAddress = new Uri(endpoint.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        _clientsByCatalog.Add(catalogId, new RestGeoCoreClient(httpClient));
    }

    public IReadOnlyCollection<string> RegisteredCatalogIds => _clientsByCatalog.Keys;

    public IGeoCoreClient Resolve(string? catalogInstanceId = null, Guid organizationId = default)
    {
        if (TryResolve(catalogInstanceId, organizationId, out var client))
        {
            return client;
        }

        throw new KeyNotFoundException(
            $"No registered IGeoCoreClient matched catalogInstanceId='{catalogInstanceId ?? "<null>"}', organizationId='{organizationId}'.");
    }

    public bool TryResolve(
        string? catalogInstanceId,
        Guid organizationId,
        [NotNullWhen(true)] out IGeoCoreClient? client)
    {
        if (catalogInstanceId is not null)
        {
            if (string.IsNullOrWhiteSpace(catalogInstanceId))
            {
                throw new ArgumentException(
                    "Catalog instance ID cannot be empty or whitespace.",
                    nameof(catalogInstanceId));
            }

            return _clientsByCatalog.TryGetValue(catalogInstanceId, out client);
        }

        if (organizationId != Guid.Empty &&
            _catalogByOrg.TryGetValue(organizationId, out var mappedCatalogId))
        {
            return _clientsByCatalog.TryGetValue(mappedCatalogId, out client);
        }

        if (!string.IsNullOrWhiteSpace(_defaultCatalogId))
        {
            return _clientsByCatalog.TryGetValue(_defaultCatalogId, out client);
        }

        client = null;
        return false;
    }
}
