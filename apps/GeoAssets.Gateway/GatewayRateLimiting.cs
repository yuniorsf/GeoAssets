using System.Threading.RateLimiting;
using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.RateLimiting;

namespace GeoAssets.Gateway;

/// <summary>
/// Per-<c>OrganizationId</c> token-bucket rate limiting at the gateway edge (XD01-214) — mitigates
/// the "noisy neighbor" risk (e.g. a contractor's batch mobile photo upload) degrading other
/// tenants sharing the same downstream clusters, before their traffic ever reaches a backend.
///
/// Every <see cref="OrganizationIdPartitionKeyResolver.Resolve"/> partition gets its own
/// independent <see cref="TokenBucketRateLimiterOptions"/> instance (the token-bucket limiter
/// factory passed to <see cref="RateLimitPartition.GetTokenBucketLimiter{TKey}"/> runs once per
/// distinct key), so one organization exhausting its bucket can never consume another's tokens.
/// </summary>
internal static class GatewayRateLimiting
{
    public static IServiceCollection AddTenantRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var tokenLimit = configuration.GetValue("RateLimiting:TokenLimit", defaultValue: 100);
        var tokensPerPeriod = configuration.GetValue("RateLimiting:TokensPerPeriod", defaultValue: 100);
        var replenishmentPeriodSeconds = configuration.GetValue("RateLimiting:ReplenishmentPeriodSeconds", defaultValue: 10);

        // Entra CIAM doesn't emit an organization claim out of the box (ClaimMapping.EntraDefault
        // leaves OrganizationIdClaimTypes empty) — same convention as HttpTenantContext in
        // GeoAssets.Server/GeoAssets.FieldService.Server. Until an operator configures this, every
        // caller resolves to OrganizationIdPartitionKeyResolver.UnresolvedPartitionKey and shares
        // one bucket, which is a safe (if less useful) default rather than a broken one.
        // Only OrganizationIdClaimTypes is ever read (OrganizationIdPartitionKeyResolver doesn't
        // need ObjectId/Email/DisplayName/Role), so a minimal ClaimMapping is enough here —
        // ClaimMapping is a plain class with init-only properties, not a record, so there's no
        // "with" expression to copy ClaimMapping.EntraDefault's other fields from.
        var organizationIdClaimType = configuration["RateLimiting:OrganizationIdClaimType"];
        var claimMapping = string.IsNullOrWhiteSpace(organizationIdClaimType)
            ? ClaimMapping.EntraDefault
            : new ClaimMapping { OrganizationIdClaimTypes = [organizationIdClaimType] };

        services.AddRateLimiter(options =>
        {
            options.OnRejected = (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                return ValueTask.CompletedTask;
            };

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var partitionKey = OrganizationIdPartitionKeyResolver.Resolve(httpContext.Request, claimMapping);
                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
                {
                    TokenLimit = tokenLimit,
                    TokensPerPeriod = tokensPerPeriod,
                    ReplenishmentPeriod = TimeSpan.FromSeconds(replenishmentPeriodSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });
        });

        return services;
    }
}
