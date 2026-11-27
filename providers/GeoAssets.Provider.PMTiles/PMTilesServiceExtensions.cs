using GeoAssets.Core.Interfaces;
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
    /// (<see cref="PMTilesPackStorage"/>) — a host's composition root names its own folder (MAUI: under its app data).
    /// Without it, a pack is read where it was picked.
    /// </summary>
    public static IServiceCollection AddPMTilesPackStorage(this IServiceCollection services, string directory)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new PMTilesPackStorage(directory));
        return services;
    }
}
