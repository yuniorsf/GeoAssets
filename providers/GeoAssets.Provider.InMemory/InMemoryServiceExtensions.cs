using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.InMemory;

public static class InMemoryServiceExtensions
{
    /// <summary>
    /// Registers the in-memory provider pool and its plugin so the boot dialog
    /// and pool panel can discover it alongside external providers.
    /// </summary>
    public static IServiceCollection AddGeoAssetsInMemory(this IServiceCollection services)
    {
        services.AddSingleton<IProviderPool, InMemoryProviderPool>();
        services.AddSingleton<IProviderPlugin, InMemoryProviderPlugin>();
        return services;
    }
}
