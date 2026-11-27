using GeoAssets.Core.Services;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Server;

/// <summary>
/// The caller's organization from identity membership (XD01-471). Entra CIAM tokens carry no organization claim, so
/// without it every caller resolves to no organization and reads only the shared features: on a distributor silo, whose
/// features all belong to the company's organization, WMS and the map draw nothing.
/// </summary>
/// <remarks>
/// Off by default (<see cref="EnabledKey"/>): an organization's provider refuses writes to shared rows, so turning it on
/// where users who have an organization edit shared features stops those edits and makes their new features
/// organization-owned. The distributor silos turn it on.
/// </remarks>
public static class CallerMembershipOrganizationExtensions
{
    /// <summary>The setting that turns the resolution on (a boolean, default <c>false</c>).</summary>
    public const string EnabledKey = "GeoAssets:Tenancy:OrganizationFromMembership";

    /// <summary>Registers the request's <see cref="CallerMembershipOrganization"/>, empty until the middleware sets it.</summary>
    public static IServiceCollection AddCallerMembershipOrganization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<CallerMembershipOrganization>();
        return services;
    }

    /// <summary>
    /// When <see cref="EnabledKey"/> is on, records the organization of an authenticated caller whose token carries none —
    /// read from the request's <see cref="AuthorizationContext"/>, which authorization already loaded — for an active user
    /// who has one. A <c>GeoAssets.ServiceCaller</c> service principal, an inactive user and a user without an organization
    /// stay organization-less. Call
    /// after authentication and authorization, before anything reads the caller's catalog.
    /// </summary>
    public static IApplicationBuilder UseCallerMembershipOrganization(this IApplicationBuilder app, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetValue<bool>(EnabledKey) ? app.Use(ResolveAsync) : app;
    }

    private static async Task ResolveAsync(HttpContext context, RequestDelegate next)
    {
        // A GeoAssets.ServiceCaller service principal has no membership: its organization is the one kernel code enters for
        // the unit of work, and its policies never consult the identity database, so neither does this (no JIT record).
        if (context.User.Identity?.IsAuthenticated == true && !context.User.IsInRole(GeoAuthorizationHandler.ServiceCallerAppRole))
        {
            var services = context.RequestServices;
            var caller = services.GetService<ICurrentUserAccessor>() is { } accessor
                ? await accessor.GetCurrentUserAsync(context.RequestAborted).ConfigureAwait(false)
                : null;

            if (caller is { OrganizationId: var claimed, ExternalObjectId.Length: > 0 }
                && claimed == Guid.Empty
                && services.GetService<IGeoAuthorizationService>() is { } authorization)
            {
                var user = (await authorization.GetAuthorizationContextAsync(context.RequestAborted).ConfigureAwait(false)).User;
                if (user is { IsActive: true, OrganizationId: { } organizationId } && organizationId != Guid.Empty)
                    services.GetRequiredService<CallerMembershipOrganization>().Set(organizationId);
            }
        }

        await next(context).ConfigureAwait(false);
    }
}
