using GeoAssets.Core.Models;

namespace GeoAssets.Core.OfflinePacks;

/// <summary>
/// The offline map packs a host keeps on its device (XD01-464; MAUI: the <c>pmtiles</c> provider's pack storage) — what
/// a field user sees and frees before going offline. Implemented by the provider that stores the packs; registered only
/// where packs are stored.
/// </summary>
public interface IOfflineMapPackCatalog
{
    /// <summary>Every stored pack with its metadata and the entries that draw it, and the storage they use together.</summary>
    Task<OfflineMapPackListing> ListAsync(CancellationToken ct);

    /// <summary>
    /// Deletes the pack <paramref name="packId"/>: each provider-pool entry that draws it is taken off the map by
    /// <paramref name="removeFromMap"/> (awaited on the caller's context), removed from the pool and its provider closed;
    /// then the pack's file and its cached tiles are deleted. Nothing changes when the pool's active entry draws it
    /// (<see cref="OfflineMapPackDeletion.InUseByActiveEntry"/>) or no such pack is stored.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="packId"/> is not a pack id.</exception>
    Task<OfflineMapPackDeletion> DeleteAsync(string packId, Func<ProviderEntry, Task> removeFromMap, CancellationToken ct);
}
