namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// Opportunistic offline basemap tile cache (XD01-243) — tiles are cached as they're viewed
/// online, not pre-seeded for a bounding box/zoom range ahead of time. Pre-seeding a work area
/// before a crew goes offline is a product decision (which area, which zoom levels, how much
/// device storage to spend) out of this scaffolding ticket's scope; it can be layered on top of
/// this cache without changing its contract.
/// </summary>
public interface ILocalVectorTileCache
{
    Task<LocalVectorTile?> GetAsync(int z, int x, int y, CancellationToken ct = default);

    Task PutAsync(LocalVectorTile tile, CancellationToken ct = default);
}
