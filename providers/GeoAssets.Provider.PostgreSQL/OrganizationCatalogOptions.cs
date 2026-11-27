namespace GeoAssets.Provider.PostgreSQL;

/// <summary>
/// How <see cref="Repositories.PostgresOrganizationAssetCatalogs"/> holds each organization's features (XD01-449),
/// configuration section <see cref="SectionName"/>. An organization with at most <see cref="FullCacheMaxFeatures"/> own
/// features is cached whole in memory, as before; a larger one is served from PostgreSQL, with a by-id cache of
/// <see cref="ByIdCacheCapacity"/> features and reads that enumerate refused beyond <see cref="EnumerationLimit"/>
/// features. XD01-448 measured about 9.3 KiB of memory per cached feature.
/// </summary>
public sealed class OrganizationCatalogOptions
{
    public const string SectionName = "GeoAssets:OrganizationCatalogs";

    /// <summary>The most own features an organization may have and still be cached whole; below 0 counts as 0 (never cached whole).</summary>
    public int FullCacheMaxFeatures { get; set; } = 50_000;

    /// <summary>The most features one enumerating read of a store-served organization returns; below 1 counts as 1.</summary>
    public int EnumerationLimit { get; set; } = 10_000;

    /// <summary>The features a store-served organization keeps by id; below 0 counts as 0 (no by-id cache).</summary>
    public int ByIdCacheCapacity { get; set; } = 5_000;

    internal int EffectiveFullCacheMaxFeatures => Math.Max(0, FullCacheMaxFeatures);

    internal int EffectiveEnumerationLimit => Math.Max(1, EnumerationLimit);

    internal int EffectiveByIdCacheCapacity => Math.Max(0, ByIdCacheCapacity);
}
