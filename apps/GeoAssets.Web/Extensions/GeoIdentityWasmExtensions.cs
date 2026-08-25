using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Shared.Services;
using GeoAssets.Web.Services.Identity;
using GeoAssets.Web.Services.Identity.InMemory;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Web.Extensions;

/// <summary>
/// DI registration for the GeoAssets identity stack's local-dev/demo mode in Blazor
/// WebAssembly — in-memory repositories (no EF Core / no direct DB access from browser),
/// seeded with canonical roles/permissions/policies and JIT-provisioning new users on login.
///
/// This is no longer the production path (XD01-18) — production points
/// <see cref="IGeoAuthorizationService"/> at the real server-side identity backend instead
/// (see <c>GeoIdentityRestExtensions.AddGeoIdentityRest</c>), switched via
/// <c>"Identity:Backend"</c> config ("InMemory" default | "Rest"), mirroring how
/// <c>"ServiceOrders:Backend"</c> already switches that module. <c>ICurrentUserAccessor</c>
/// is registered once in Program.cs regardless of backend, since both need to know "who is
/// the current user" from the same MSAL auth state.
///
/// Deliberately does not register <c>IRoleAssignmentProvider</c>/<c>IRoleSyncStatusProvider</c>
/// (XD01-63) — this backend has no server round-trip, so role sync can never be functional here;
/// the admin UI resolves both optionally and hides its "Register in Entra"/"Assign in Entra"
/// controls when they're absent.
/// </summary>
public static class GeoIdentityWasmExtensions
{
    public static IServiceCollection AddGeoIdentityWasmDev(this IServiceCollection services)
    {
        // Shared in-memory store (singleton — lives for the lifetime of the WASM session)
        services.AddSingleton<WasmIdentityStore>();
        services.AddSingleton<IdentitySeeder>();

        // In-memory repository implementations
        services.AddScoped<IOrganizationRepository, InMemoryOrganizationRepository>();
        services.AddScoped<IGroupRepository,        InMemoryGroupRepository>();
        services.AddScoped<IUserRepository,         InMemoryUserRepository>();
        services.AddScoped<IRoleRepository,         InMemoryRoleRepository>();
        services.AddScoped<IPermissionRepository,   InMemoryPermissionRepository>();
        services.AddScoped<IUserClaimRepository,    InMemoryUserClaimRepository>();
        services.AddScoped<IPolicyRepository,       InMemoryPolicyRepository>();
        services.AddScoped<IPendingInvitationRepository, InMemoryPendingInvitationRepository>();

        // Authorization service
        services.AddScoped<IGeoAuthorizationService, GeoAuthorizationService>();

        // JIT user provisioning on auth state change — only meaningful against a writable
        // (in-memory) user repository; the Rest backend has no provisioning endpoint (XD01-12's
        // federated-auth/JIT-provisioning follow-up covers that for the real backend).
        // Scoped (not singleton) because AuthenticationStateProvider in WASM
        // depends on scoped MSAL options. In Blazor WASM, host.Services is the
        // root scope so this scoped service lives for the entire session.
        services.AddScoped<UserProvisioningService>();

        return services;
    }
}
