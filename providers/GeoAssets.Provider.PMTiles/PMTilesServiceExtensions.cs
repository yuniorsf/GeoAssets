using GeoAssets.Core.Interfaces;
using GeoAssets.Core.OfflinePacks;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PMTiles.Packs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Provider.PMTiles;

/// <summary>Registrations of the PMTiles provider (XD01-438).</summary>
public static class PMTilesServiceExtensions
{
    /// <summary>
    /// Registers the <c>pmtiles</c> provider plugin (offline map packs) — what <see cref="PMTilesProviderPluginModule"/>
    /// registers. A host composing providers through discovery runs the module instead; never both (duplicate plugin id).
    /// </summary>
    public static IServiceCollection AddGeoAssetsPMTilesPacks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IProviderPlugin, PMTilesPackProviderPlugin>();
        return services;
    }

    /// <summary>
    /// Keeps the offline map packs the <c>pmtiles</c> plugin imports in <paramref name="directory"/>
    /// (<see cref="PMTilesPackStorage"/>) — a host's composition root names its own folder (MAUI: under its app data) —
    /// asking the user before it copies a pack larger than <paramref name="largePackBytes"/> (XD01-465). Without it, a
    /// pack is read where it was picked. Also registers the packs' <see cref="IOfflineMapPackCatalog"/> (XD01-464,
    /// <see cref="PMTilesOfflinePackCatalog"/>, scoped): over the host's <see cref="IProviderPool"/>, evicting a deleted
    /// pack's tiles where the host registers an <see cref="ITileLayerVersionEviction"/>; import times come from the host's
    /// <see cref="TimeProvider"/> (the system clock without one).
    /// </summary>
    public static IServiceCollection AddPMTilesPackStorage(
        this IServiceCollection services, string directory, long largePackBytes = PMTilesPackStorage.DefaultLargePackBytes)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(largePackBytes);
        services.TryAddSingleton(sp => new PMTilesPackStorage(directory, largePackBytes, sp.GetService<TimeProvider>()));
        services.TryAddScoped<IOfflineMapPackCatalog>(sp => new PMTilesOfflinePackCatalog(
            sp.GetRequiredService<PMTilesPackStorage>(),
            sp.GetRequiredService<IProviderPool>(),
            sp.GetService<ITileLayerVersionEviction>()));
        return services;
    }
}
