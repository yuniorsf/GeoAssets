using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server;

/// <summary>
/// Wires <see cref="IRoleAssignmentProvider"/> (XD01-59 Phase 2) — defaults to
/// <see cref="NullRoleAssignmentProvider"/> (a no-op) unless a different provider is passed,
/// mirroring <see cref="GeoAssetsAuthenticationExtensions.AddGeoAssetsAuthentication"/>'s
/// optional-provider-argument pattern. Swapping in a real, vendor-backed provider (e.g. the
/// Graph-backed implementation tracked as XD01-62) is then a DI argument at this call site, not
/// a change to whatever eventually consumes <see cref="IRoleAssignmentProvider"/>.
/// </summary>
public static class GeoAssetsRoleAssignmentExtensions
{
    public static IServiceCollection AddRoleAssignmentProvider(
        this IServiceCollection services,
        IConfiguration          configuration,
        IRoleAssignmentProvider? provider = null)
    {
        services.AddSingleton(provider ?? new NullRoleAssignmentProvider());

        return services;
    }
}
