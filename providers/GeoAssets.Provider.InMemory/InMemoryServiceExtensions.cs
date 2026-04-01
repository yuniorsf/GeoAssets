using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.InMemory;

public static class InMemoryServiceExtensions
{
    /// <summary>
    /// Registers the provider pool and the in-memory plugin so it appears
    /// in the boot dialog and pool panel alongside external providers.
    /// </summary>
    public static IServiceCollection AddGeoAssetsInMemory(this IServiceCollection services)
    {
        services.AddSingleton<IProviderPool, ProviderPool>();
        services.AddSingleton<IProviderPlugin, InMemoryProviderPlugin>();
        return services;
    }
}
