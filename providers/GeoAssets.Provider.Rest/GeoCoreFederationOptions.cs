namespace GeoAssets.Provider.Rest;

/// <summary>
/// Options binding multi-catalog <c>GeoAssets.Server</c> shard endpoints for <see cref="FederatedGeoCoreClientResolver"/>.
/// </summary>
public sealed class GeoCoreFederationOptions
{
    public const string SectionName = "GeoCoreFederation";

    public string? DefaultCatalogInstanceId { get; set; }

    public Dictionary<string, GeoCoreCatalogEndpoint> Catalogs { get; init; } = new(StringComparer.Ordinal);
}
