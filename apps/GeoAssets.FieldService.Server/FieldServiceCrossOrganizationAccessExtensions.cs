using GeoAssets.Workflow.Persistence;

namespace GeoAssets.FieldService.Server;

/// <summary>DI registration of <see cref="FieldServiceCrossOrganizationAccess"/> (XD01-328).</summary>
internal static class FieldServiceCrossOrganizationAccessExtensions
{
    /// <summary>
    /// Registers the scoped <see cref="FieldServiceCrossOrganizationAccess"/> and the grant-aware read path it uses
    /// (<see cref="EFServiceOrderRepository"/> as <see cref="ICrossOrganizationServiceOrderReader"/>, over the request's
    /// <see cref="ServiceOrderDbContext"/>). Needs <see cref="ServiceOrderDbContext"/>, <c>ServiceOrderRules</c>
    /// (<c>AddServiceOrderRules</c>) and <see cref="IMirroredCrossOrganizationEntitlements"/> (<c>AddScopedRoleAssignmentSync</c>).
    /// </summary>
    public static IServiceCollection AddFieldServiceCrossOrganizationAccess(this IServiceCollection services)
    {
        services.AddScoped<ICrossOrganizationServiceOrderReader>(sp =>
            new EFServiceOrderRepository(sp.GetRequiredService<ServiceOrderDbContext>()));
        services.AddScoped<FieldServiceCrossOrganizationAccess>();
        return services;
    }
}
