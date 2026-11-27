using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.OfflinePacks;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.PMTiles.Packs;

/// <summary>
/// <see cref="IOfflineMapPackCatalog"/> over a <see cref="PMTilesPackStorage"/> (XD01-464): lists its packs with their
/// header's bounds and zooms and the <see cref="IProviderPool"/> entries drawing them, and deletes a pack whole.
/// <para>
/// An entry draws a pack when its provider (through the decorator chain) is the <c>pmtiles</c> plugin's
/// <see cref="PMTilesPackAssetProvider"/> or <see cref="PMTilesRasterPackAssetProvider"/> over the stored file, or when
/// it is a <c>pmtiles</c> entry whose <see cref="PMTilesPackProviderPlugin.PackPathKey"/> is that file.
/// </para>
/// <para>
/// Deleting refuses a pack the active entry draws (the pool keeps its active entry). Otherwise each entry drawing it
/// is taken off the map by the caller's callback, removed from the pool and its provider disposed — which closes the
/// pack's file handle; a read under way completes, the map asks for no more — then the file and its import record are
/// deleted (<see cref="PMTilesPackStorage.Delete"/>; the file is shared for deleting, so a reader outside the pool
/// doesn't block it), and last the pack's tiles are evicted from the host's tile cache
/// (<see cref="ITileLayerVersionEviction"/>: scope <see cref="PMTilesPackTiles.Scope"/>, layer set
/// <see cref="PMTilesPackTiles.LayerSetId"/>, version = the pack's id). A raster pack has no cached tiles.
/// </para>
/// </summary>
public sealed class PMTilesOfflinePackCatalog : IOfflineMapPackCatalog
{
    private readonly PMTilesPackStorage _storage;
    private readonly IProviderPool _pool;
    private readonly ITileLayerVersionEviction? _tileEviction;

    /// <summary>A catalog of <paramref name="storage"/>'s packs, drawn by <paramref name="pool"/>'s entries, their tiles cached where <paramref name="tileEviction"/> evicts.</summary>
    public PMTilesOfflinePackCatalog(PMTilesPackStorage storage, IProviderPool pool, ITileLayerVersionEviction? tileEviction = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(pool);
        _storage = storage;
        _pool = pool;
        _tileEviction = tileEviction;
    }

    public async Task<OfflineMapPackListing> ListAsync(CancellationToken ct)
    {
        var entries = _pool.All.ToArray();
        var stored = await _storage.ListAsync(ct).ConfigureAwait(false);
        var packs = stored.Select(pack => Describe(pack, entries)).ToArray();
        return new OfflineMapPackListing(packs, stored.Sum(pack => pack.SizeBytes));
    }

    public async Task<OfflineMapPackDeletion> DeleteAsync(string packId, Func<ProviderEntry, Task> removeFromMap, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(removeFromMap);
        var path = _storage.PathOf(packId);
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(path))
            return OfflineMapPackDeletion.NotFound;

        var users = _pool.All.Where(entry => Draws(entry, path)).ToArray();
        if (users.Any(entry => entry.IsActive))
            return OfflineMapPackDeletion.InUseByActiveEntry;

        foreach (var entry in users)
        {
            // The map and the pool belong to the caller's (UI) context: stay on it until the entries are gone.
            await removeFromMap(entry).ConfigureAwait(true);
            _pool.Remove(entry.Id);
            (entry.Provider.UnwrapToConcrete() as IDisposable)?.Dispose();
        }

        _storage.Delete(packId);
        if (_tileEviction is not null)
            await _tileEviction.RemoveLayerVersionAsync(PMTilesPackTiles.Scope, PMTilesPackTiles.LayerSetId, packId, ct).ConfigureAwait(false);
        return OfflineMapPackDeletion.Deleted;
    }

    private static OfflineMapPack Describe(PMTilesStoredPack pack, ProviderEntry[] entries)
    {
        var users = entries
            .Where(entry => Draws(entry, pack.Path))
            .Select(entry => new OfflineMapPackUse(entry.Id, entry.Name, entry.IsActive))
            .ToArray();
        var header = pack.Header;
        var name = users.Length > 0 && !string.IsNullOrWhiteSpace(users[0].Name)
            ? users[0].Name
            : string.IsNullOrWhiteSpace(pack.FileName) ? pack.Id : Path.GetFileNameWithoutExtension(pack.FileName);

        return new OfflineMapPack(
            pack.Id,
            name,
            pack.FileName,
            pack.Kind switch
            {
                PMTilesPackKind.Vector => OfflineMapPackKind.Vector,
                PMTilesPackKind.Raster => OfflineMapPackKind.Raster,
                _ => OfflineMapPackKind.Unreadable,
            },
            pack.SizeBytes,
            pack.ImportedAt,
            header is null ? null : BoundsOf(header),
            header?.MinZoom,
            header?.MaxZoom,
            users);
    }

    private static BoundingBox? BoundsOf(PMTilesHeader header)
    {
        var (west, south, east, north) = header.Bounds;
        return west < east && south < north ? new BoundingBox(west, south, east, north) : null;
    }

    private static bool Draws(ProviderEntry entry, string packPath)
    {
        var pack = entry.Provider.UnwrapToConcrete() switch
        {
            PMTilesPackAssetProvider vector => vector.Pack,
            PMTilesRasterPackAssetProvider raster => raster.Pack,
            _ => null,
        };
        if (pack is not null)
            return string.Equals(pack.Path, packPath, StringComparison.Ordinal);

        return string.Equals(entry.PluginId, PMTilesPackProviderPlugin.PluginId, StringComparison.Ordinal)
               && entry.Values.TryGetValue(PMTilesPackProviderPlugin.PackPathKey, out var path)
               && !string.IsNullOrWhiteSpace(path)
               && string.Equals(Path.GetFullPath(path), packPath, StringComparison.Ordinal);
    }
}
