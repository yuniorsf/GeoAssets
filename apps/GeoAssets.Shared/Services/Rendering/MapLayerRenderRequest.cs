using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Shared.Services.Rendering;

/// <summary>
/// One pool entry to render on one map (XD01-422). <paramref name="Source"/> is the provider to
/// read from — usually <see cref="ProviderEntry.Provider"/>, but <c>MapContainer</c> reads the
/// active entry through its decorated <see cref="IAssetProvider"/> so it keeps its instrumentation.
/// </summary>
/// <param name="MapDivId">The map's container element id.</param>
/// <param name="Entry">The pool entry; its <see cref="ProviderEntry.Id"/> names a tile layer.</param>
/// <param name="Source">The provider to read from.</param>
public sealed record MapLayerRenderRequest(string MapDivId, ProviderEntry Entry, IAssetProvider Source)
{
    /// <summary>A request that reads from the entry's own provider.</summary>
    public static MapLayerRenderRequest For(string mapDivId, ProviderEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new MapLayerRenderRequest(mapDivId, entry, entry.Provider);
    }

    /// <summary>The id of the entry's tile layer on the map — the entry's id.</summary>
    public string LayerId => Entry.Id.ToString();
}
