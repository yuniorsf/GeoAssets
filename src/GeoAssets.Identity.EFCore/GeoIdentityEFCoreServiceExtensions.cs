using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.EFCore;
using GeoAssets.Identity.Authorization.EFCore.Repositories;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Identity;

/// <summary>
/// DI registration helpers for the GeoAssets identity and authorization stack.
/// </summary>
public static class GeoIdentityEFCoreServiceExtensions
{
    /// <summary>
    /// Registers the EF Core implementation of the identity repositories
    /// and the authorization service.
    ///
    /// The caller must also register the DB provider:
    /// <code>
    ///   services.AddGeoIdentity(o => o.UseSqlServer(connectionString));
    ///   // or
    ///   services.AddGeoIdentity(o => o.UseSqlite("Data Source=identity.db"));
    /// </code>
    ///
    /// Authentication (ICurrentUserAccessor) is NOT registered here because
    /// it depends on the host type (ASP.NET Core, MAUI, etc.).
    /// Register it separately:
    /// <code>
    ///   // ASP.NET Core / Blazor Server
    ///   services.AddHttpContextAccessor();
    ///   services.AddScoped&lt;ICurrentUserAccessor&gt;(sp =>
    ///       new ClaimsPrincipalCurrentUserAccessor(
    ///           () => sp.GetRequiredService&lt;IHttpContextAccessor&gt;().HttpContext?.User));
    /// </code>
    /// </summary>
    public static IServiceCollection AddGeoIdentity(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<GeoIdentityDbContext>(configureDb);

        services.AddScoped<IUserRepository,       EFUserRepository>();
        services.AddScoped<IRoleRepository,       EFRoleRepository>();
        services.AddScoped<IPermissionRepository, EFPermissionRepository>();
        services.AddScoped<IUserClaimRepository,  EFUserClaimRepository>();
        services.AddScoped<IPolicyRepository,     EFPolicyRepository>();

        services.AddScoped<IGeoAuthorizationService, GeoAuthorizationService>();

        return services;
    }
}
