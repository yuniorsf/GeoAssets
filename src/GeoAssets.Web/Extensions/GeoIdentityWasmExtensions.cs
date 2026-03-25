using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Web.Services.Identity;
using GeoAssets.Web.Services.Identity.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Web.Extensions;

/// <summary>
/// DI registration for the GeoAssets identity stack in Blazor WebAssembly.
///
/// Uses in-memory repository implementations (no EF Core / no direct DB access from browser).
/// For production, replace the in-memory repos with HTTP client implementations
/// that call a secured backend API.
/// </summary>
public static class GeoIdentityWasmExtensions
{
    public static IServiceCollection AddGeoIdentityWasm(this IServiceCollection services)
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

        // Authorization service
        services.AddScoped<IGeoAuthorizationService, GeoAuthorizationService>();

        // Current user accessor (reads from Blazor MSAL auth state)
        services.AddScoped<ICurrentUserAccessor, BlazorWasmCurrentUserAccessor>();

        // JIT user provisioning on auth state change.
        // Scoped (not singleton) because AuthenticationStateProvider in WASM
        // depends on scoped MSAL options. In Blazor WASM, host.Services is the
        // root scope so this scoped service lives for the entire session.
        services.AddScoped<UserProvisioningService>();

        return services;
    }
}
