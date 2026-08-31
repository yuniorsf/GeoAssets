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
/// <c>UserProvisioningService</c>: it has no server round-trip to provision against, and — as
/// of XD01-88 — isn't the mechanism for this backend anyway. JIT provisioning against Rest is
/// instead the server's own job, inside <c>GeoAuthorizationService.GetAuthorizationContextAsync</c>
/// (XD01-88; previously a real gap, tracked at the time under a stale "XD01-12" citation —
/// XD01-12 was JWT bearer-token validation only. Federation/organization-resolution scope some
/// earlier notes also pointed at here was XD01-49, resolved Done-without-implementing: this
/// system's registration is permanently invitation-only, so that scope never applied).
///
/// Also registers <see cref="IRoleAssignmentProvider"/>/<see cref="IRoleSyncStatusProvider"/>
/// (XD01-63) — thin HTTP proxies to the server's own <see cref="IRoleAssignmentProvider"/>
/// (XD01-62, which may itself resolve to a no-op depending on the server's <c>RoleSync:Enabled</c>
/// config; <see cref="IRoleSyncStatusProvider"/> is how the UI tells which). Deliberately not
/// registered by <c>GeoIdentityWasmExtensions.AddGeoIdentityWasmDev</c> — the in-memory backend
/// has no server round-trip, so role sync can never be functional there.
///
/// Also registers <see cref="IPendingInvitationRepository"/>/<see cref="IUserClaimRepository"/>
/// (XD01-70) — <c>GeoIdentityWasmExtensions.AddGeoIdentityWasmDev</c> registers its own
/// in-memory implementations of both separately, so these Rest ones are never used there.
///
/// Also registers <see cref="IInvitationStatusProvider"/>/<see cref="IInvitationClient"/>
/// (XD01-71) — the create/revoke/redeem business operations and the "is this really
/// Graph/ACS-backed" status check that sit alongside <see cref="IPendingInvitationRepository"/>'s
/// plain CRUD. Same reasoning as role sync: never registered under
/// <c>GeoIdentityWasmExtensions.AddGeoIdentityWasmDev</c>.
///
/// Also registers <see cref="IOrganizationRepository"/>/<see cref="IGroupRepository"/>
/// (XD01-128) — full admin CRUD against the server's <c>/organizations</c>/<c>/groups</c>
/// endpoints, closing the gap left by <c>GeoIdentityWasmExtensions.AddGeoIdentityWasmDev</c>'s
/// in-memory-only equivalents. <see cref="IPolicyRepository"/> is also registered here for the
/// first time — like <see cref="IPermissionRepository"/>, policies are code-seeded and read-only
/// (<c>GeoIdentitySeeder.SeedPoliciesAsync</c>), so <c>RestPolicyRepository</c> only implements
/// the one existing <c>GET /policies</c> endpoint.
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

        services.AddScoped<IOrganizationRepository>(sp =>
            new RestOrganizationRepository(CreateIdentityClient(sp)));

        services.AddScoped<IGroupRepository>(sp =>
            new RestGroupRepository(CreateIdentityClient(sp)));

        services.AddScoped<IPolicyRepository>(sp =>
            new RestPolicyRepository(CreateIdentityClient(sp)));

        services.AddScoped<IRoleAssignmentProvider>(sp =>
            new RestRoleAssignmentProvider(CreateIdentityClient(sp)));

        services.AddScoped<IRoleSyncStatusProvider>(sp =>
            new RestRoleSyncStatusProvider(CreateIdentityClient(sp)));

        services.AddScoped<IPendingInvitationRepository>(sp =>
            new RestPendingInvitationRepository(CreateIdentityClient(sp)));

        services.AddScoped<IUserClaimRepository>(sp =>
            new RestUserClaimRepository(CreateIdentityClient(sp)));

        services.AddScoped<IInvitationStatusProvider>(sp =>
            new RestInvitationStatusProvider(CreateIdentityClient(sp)));

        services.AddScoped<IInvitationClient>(sp =>
            new RestInvitationClient(CreateIdentityClient(sp)));

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
