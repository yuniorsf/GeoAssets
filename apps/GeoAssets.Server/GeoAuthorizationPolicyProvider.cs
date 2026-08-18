using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace GeoAssets.Server;

/// <summary>
/// Resolves any policy name passed to <c>.RequireAuthorization("SomeName")</c> into an
/// ASP.NET Core <see cref="AuthorizationPolicy"/> requiring an authenticated user plus a
/// <see cref="GeoPolicyRequirement"/> for that name (XD01-13) — bridging the
/// <c>AppPolicy</c>/<c>PolicyRequirement</c> engine already used by the Blazor client
/// (<c>GeoAuthorizationService.EvaluatePolicyAsync</c>) into the native authorization
/// pipeline, so server endpoints and client UI share one source of truth for policy
/// definitions instead of re-encoding them as ASP.NET Core policies by hand.
///
/// Policy names aren't validated here against the real <c>AppPolicy</c> table — that data
/// lives in Postgres and is looked up per-request by <see cref="GeoAuthorizationHandler"/>.
/// An unknown name (typo, deleted policy) fails closed at evaluation time rather than at
/// startup; see that handler's doc comment.
///
/// <see cref="GetDefaultPolicyAsync"/>/<see cref="GetFallbackPolicyAsync"/> delegate to the
/// framework's own <see cref="DefaultAuthorizationPolicyProvider"/> so the global
/// <c>FallbackPolicy</c> set in <c>AddGeoAssetsAuthentication</c> (XD01-12) keeps applying
/// to endpoints that don't name a policy at all.
/// </summary>
public sealed class GeoAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallback = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new GeoPolicyRequirement(policyName))
            .Build();

        return Task.FromResult<AuthorizationPolicy?>(policy);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _fallback.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();
}
