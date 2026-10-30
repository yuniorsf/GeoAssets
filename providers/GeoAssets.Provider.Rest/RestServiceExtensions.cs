using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.Rest;

public static class RestServiceExtensions
{
    /// <summary>
    /// Registers the REST provider factory so <c>ProviderPoolPanel</c> discovers it
    /// and shows a 🔌 REST connection button.
    ///
    /// On the server side, call <c>app.MapGeoAssetsApi()</c> (from
    /// <c>GeoAssets.Provider.PostgreSQL</c>) to expose the API endpoints.
    /// </summary>
    public static IServiceCollection AddGeoAssetsRest(this IServiceCollection services)
    {
        services.AddHttpClient();
        services.AddSingleton<RestProviderFactory>();
        // Keep legacy factory registration for any code that still resolves IExternalProviderFactory
        services.AddSingleton<IExternalProviderFactory>(
            sp => sp.GetRequiredService<RestProviderFactory>());
        // Plugin registration — discovered by ProviderPluginRegistry and the boot/pool UI
        services.AddSingleton<IProviderPlugin, RestProviderPlugin>();
        return services;
    }

    /// <summary>
    /// Registers federated <see cref="IGeoCoreClientResolver"/> and default <see cref="IGeoCoreClient"/>
    /// bound from <c>configuration["GeoCoreFederation"]</c>.
    /// </summary>
    public static IServiceCollection AddFederatedGeoCoreRestClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddHttpClient();
        services.Configure<GeoCoreFederationOptions>(opts =>
            configuration.GetSection(GeoCoreFederationOptions.SectionName).Bind(opts));
        services.AddSingleton<IGeoCoreClientResolver, FederatedGeoCoreClientResolver>();
        services.AddSingleton<IGeoCoreClient>(sp =>
            sp.GetRequiredService<IGeoCoreClientResolver>().Resolve());

        return services;
    }

    /// <summary>
    /// Registers federated <see cref="IGeoCoreClientResolver"/> and default <see cref="IGeoCoreClient"/>
    /// configured via <paramref name="configure"/>.
    /// </summary>
    public static IServiceCollection AddFederatedGeoCoreRestClients(
        this IServiceCollection services,
        Action<GeoCoreFederationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddHttpClient();
        services.Configure(configure);
        services.AddSingleton<IGeoCoreClientResolver, FederatedGeoCoreClientResolver>();
        services.AddSingleton<IGeoCoreClient>(sp =>
            sp.GetRequiredService<IGeoCoreClientResolver>().Resolve());

        return services;
    }

    /// <summary>
    /// Registers the stateless <see cref="IGeoCoreClient"/> (<see cref="RestGeoCoreClient"/>)
    /// pointed at <paramref name="baseUrl"/> (e.g. <c>http://localhost:5000/api/geoassets</c>)
    /// for vertical microservices querying the Core GeoAssets Server.
    /// </summary>
    public static IServiceCollection AddGeoCoreRestClient(this IServiceCollection services, string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        return services.AddFederatedGeoCoreRestClients(opts =>
        {
            opts.DefaultCatalogInstanceId = "default";
            opts.Catalogs["default"] = new GeoCoreCatalogEndpoint
            {
                CatalogInstanceId = "default",
                BaseUrl = baseUrl,
            };
        });
    }
}
