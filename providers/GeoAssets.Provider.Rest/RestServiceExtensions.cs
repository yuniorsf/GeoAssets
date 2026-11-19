using GeoAssets.Core.Interfaces;
using GeoAssets.Identity.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.Rest;

public static class RestServiceExtensions
{
    private const string OrganizationScopedHttpClientName = "GeoAssetsServer:organization-scoped";

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

    /// <summary>
    /// Registers <see cref="IOrganizationScopedGeoCoreClient"/> (<see cref="RestOrganizationScopedGeoCoreClient"/>)
    /// against the same Core GeoAssets Server <paramref name="baseUrl"/> as
    /// <see cref="AddGeoCoreRestClient(IServiceCollection, string)"/> (e.g.
    /// <c>http://localhost:5000/api/geoassets</c>), authenticating with the same registered
    /// <see cref="IServiceAuthTokenProvider"/> (XD01-386). Its routes accept only a service-caller
    /// token, so register a token provider wherever this client is used for real.
    /// </summary>
    public static IServiceCollection AddOrganizationScopedGeoCoreClient(this IServiceCollection services, string baseUrl)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress) ||
            (!string.Equals(baseAddress.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(baseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"'{baseUrl}' is not an absolute HTTP(S) URL.", nameof(baseUrl));
        }

        services.AddHttpClient(OrganizationScopedHttpClientName, client => client.BaseAddress = baseAddress);
        services.AddTransient<IOrganizationScopedGeoCoreClient>(sp => new RestOrganizationScopedGeoCoreClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(OrganizationScopedHttpClientName),
            sp.GetService<IServiceAuthTokenProvider>()));
        return services;
    }
}
