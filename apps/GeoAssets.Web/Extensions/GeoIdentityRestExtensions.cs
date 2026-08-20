using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Web.Services.Identity.Rest;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Web.Extensions;

/// <summary>
/// DI registration for the GeoAssets identity stack backed by <c>GeoAssets.Server</c>'s
/// <c>/api/identity/*</c> endpoints (XD01-18) — the production alternative to
/// <see cref="GeoIdentityWasmExtensions.AddGeoIdentityWasmDev"/>'s in-memory store.
///
/// Requires <c>"GeoAssetsServer:BaseUrl"</c> configured and the <c>"GeoAssetsServer"</c>
/// named HttpClient registered with its MSAL <c>AuthorizationMessageHandler</c> (both from
/// Program.cs, XD01-17) so requests carry a bearer token the server will accept.
///
/// Also registers <see cref="IUserRepository"/>/<see cref="IRoleRepository"/>/
/// <see cref="IPermissionRepository"/> (XD01-57) for the identity admin UI (XD01-54 Phase 1)
/// to consume — previously undone because the identity API was read-only. Does not register
/// <c>UserProvisioningService</c>: JIT user provisioning against this backend isn't possible
/// (that's tracked separately, see XD01-12's federated-auth/JIT-provisioning follow-up note).
/// </summary>
public static class GeoIdentityRestExtensions
{
    public static IServiceCollection AddGeoIdentityRest(this IServiceCollection services)
    {
        services.AddScoped<IGeoAuthorizationService>(sp =>
            new RestGeoAuthorizationService(CreateIdentityClient(sp)));

        services.AddScoped<IUserRepository>(sp =>
            new RestUserRepository(CreateIdentityClient(sp)));

        services.AddScoped<IRoleRepository>(sp =>
            new RestRoleRepository(CreateIdentityClient(sp)));

        services.AddScoped<IPermissionRepository>(sp =>
            new RestPermissionRepository(CreateIdentityClient(sp)));

        return services;
    }

    private static HttpClient CreateIdentityClient(IServiceProvider sp)
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        var baseUrl = configuration["GeoAssetsServer:BaseUrl"]
            ?? throw new InvalidOperationException(
                "Identity:Backend is 'Rest' but GeoAssetsServer:BaseUrl is not configured.");

        var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeoAssetsServer");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/api/identity/");
        return client;
    }
}
