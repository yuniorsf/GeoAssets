namespace GeoAssets.Provider.Rest;

/// <summary>
/// Configuration for a single federated <c>GeoAssets.Server</c> catalog shard endpoint.
/// </summary>
public sealed class GeoCoreCatalogEndpoint
{
    public string CatalogInstanceId { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public List<Guid> OrganizationIds { get; init; } = [];
}
