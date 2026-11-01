namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// Local, opportunistically-refreshed cache of CTO splitter-port availability (XD01-276) — the
/// <c>cto_port_cache</c> table named in this ticket's AC. Same "opportunistic, not pre-seeded"
/// shape as <see cref="ILocalVectorTileCache"/>: entries are refreshed whenever the app is
/// online and reads CTO data, not proactively fetched for a work area ahead of going offline.
/// </summary>
public interface ILocalCtoPortCacheStore
{
    /// <summary>All cached CTO entries — the candidate set <c>NearestAvailableCtoAssigner</c> ranks offline.</summary>
    Task<IReadOnlyList<LocalCtoPortCacheEntry>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Upserts one CTO's cached availability, keyed by <see cref="LocalCtoPortCacheEntry.CtoFeatureId"/>.</summary>
    Task PutAsync(LocalCtoPortCacheEntry entry, CancellationToken ct = default);
}
