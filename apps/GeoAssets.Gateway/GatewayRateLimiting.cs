using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using GeoAssets.Identity.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace GeoAssets.Gateway;

/// <summary>
/// Token-bucket rate limiting at the gateway edge (XD01-214, XD01-393) — keeps one caller's burst (a contractor's batch
/// mobile photo upload, a map pan's dozens of tiles) from degrading everyone else sharing the same downstream clusters,
/// before the traffic ever reaches a backend.
/// <para>
/// One global <see cref="PartitionedRateLimiter"/>, its partition chosen per request from the policy
/// (<see cref="GatewayRateLimitPolicies.Classify"/>: <c>tiles</c> for <c>/wms</c>/<c>/wfs</c>, <c>api</c> otherwise) and
/// the caller (<see cref="RateLimitPartitionKeyResolver"/>: organization claim → token subject → client address, the
/// claims from a token <see cref="GatewayTokenValidator"/> validated when <c>AzureAdCiam</c> is configured — XD01-398 —
/// resolved by <see cref="GatewayRateLimitPartitioning"/> ahead of the limiter, whose synchronous partitioner reads it). The
/// policy is part of the partition key and each policy has its own <see cref="RateLimitBucketOptions"/>
/// (<c>RateLimiting:Api</c>, <c>RateLimiting:Tiles</c>), so a caller exhausting its tile bucket keeps its API bucket and
/// vice versa, and one caller's bucket is never another's. A single selecting limiter rather than
/// <see cref="PartitionedRateLimiter.CreateChained{TResource}"/>: chaining makes every request acquire from every limiter
/// (a tile request would also spend API tokens), the opposite of two disjoint budgets; and rather than YARP per-route
/// policies, which would change the route table and run after routing — the global limiter rejects before routing,
/// unmatched paths included. <c>/healthz</c> is never limited.
/// </para>
/// <para>
/// A rejected request is answered 429 at once (no queue) with <c>Retry-After</c> (seconds, from the bucket's own
/// estimate, else its replenishment period) and an <c>application/problem+json</c> body carrying
/// <c>code: rate-limit.exceeded</c> and the <c>policy</c>, and counted on
/// <see cref="GatewayTelemetry.RateLimitedCounterName"/>.
/// </para>
/// </summary>
internal static class GatewayRateLimiting
{
    /// <summary>The problem body's <c>code</c> of a rate-limited request.</summary>
    public const string RateLimitExceededCode = "rate-limit.exceeded";

    /// <summary>
    /// <c>RateLimiting:Api</c> when not configured — per caller (subject or client address by default): a 500-request
    /// burst for a mobile photo-upload sync, refilled at 10 requests/s. See <c>docs/perf/gateway-rate-limits.md</c>.
    /// </summary>
    public static readonly RateLimitBucketOptions DefaultApi = new(TokenLimit: 500, TokensPerPeriod: 10, ReplenishmentPeriodSeconds: 1);

    /// <summary>
    /// <c>RateLimiting:Tiles</c> when not configured — per caller, like the API's: a 1000-tile burst, sized for a few
    /// users zooming behind one NAT when tiles were anonymous (the in-app map's carry the user's token since XD01-96, so
    /// the bucket is per user now), refilled at 100 tiles/s. See <c>docs/perf/gateway-rate-limits.md</c>.
    /// </summary>
    public static readonly RateLimitBucketOptions DefaultTiles = new(TokenLimit: 1000, TokensPerPeriod: 100, ReplenishmentPeriodSeconds: 1);

    private const string HealthPath = "/healthz";
    private const string ProblemContentType = "application/problem+json";

    public static IServiceCollection AddTenantRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection("RateLimiting");

        // RateLimiting:Api:*, overridden by the pre-XD01-393 flat RateLimiting:{TokenLimit,TokensPerPeriod,
        // ReplenishmentPeriodSeconds} when a deployment still sets them, so its existing override keeps working.
        var api = RateLimitBucketOptions.Read(section, RateLimitBucketOptions.Read(section.GetSection("Api"), DefaultApi));
        var tiles = RateLimitBucketOptions.Read(section.GetSection("Tiles"), DefaultTiles);

        // Entra CIAM emits no organization claim (ClaimMapping.EntraDefault leaves OrganizationIdClaimTypes empty): until an
        // operator configures one, the key falls back to the token's subject, then to the client address. Only
        // OrganizationIdClaimTypes is read from this mapping.
        var organizationIdClaimType = section["OrganizationIdClaimType"];
        var claimMapping = string.IsNullOrWhiteSpace(organizationIdClaimType)
            ? ClaimMapping.EntraDefault
            : new ClaimMapping { OrganizationIdClaimTypes = [organizationIdClaimType] };

        services.AddMetrics();
        services.AddSingleton<GatewayRateLimitMetrics>();
        services.AddGatewayTrustedProxies(configuration);
        services.AddSingleton(claimMapping);
        AddTokenValidation(services, configuration);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, ct) => RejectAsync(context, api, tiles, ct);

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                // The gateway's own liveness probe (XD01-384) carries no token: it must never be throttled by — nor consume —
                // a bucket of the caller traffic.
                if (httpContext.Request.Path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase))
                    return RateLimitPartition.GetNoLimiter(HealthPath);

                var policy = GatewayRateLimitPolicies.Classify(httpContext.Request.Path);
                var bucket = BucketOf(policy, api, tiles);
                // The caller GatewayRateLimitPartitioning resolved (validated token claims, or the client address); without
                // that middleware in the pipeline, the decode-only key, so the partitioner never depends on it to work.
                var caller = httpContext.Items.TryGetValue(GatewayRateLimitPartitioning.CallerItemKey, out var item) && item is PartitionCaller resolved
                    ? resolved.Key
                    : RateLimitPartitionKeyResolver.Resolve(httpContext.Request, claimMapping);
                var partitionKey = policy.ToName() + "|" + caller;
                return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => bucket.ToLimiterOptions());
            });
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="GatewayTokenValidator"/> (XD01-398) and, when <c>AzureAdCiam</c> is configured, the cached CIAM
    /// metadata source it reads the issuer and signing keys from, fetched with the named
    /// <see cref="GatewayTokenValidator.MetadataHttpClientName"/> client.
    /// </summary>
    private static void AddTokenValidation(IServiceCollection services, IConfiguration configuration)
    {
        var options = GatewayTokenValidationOptions.Read(configuration);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(GatewayTokenValidator.MetadataHttpClientName);

        if (options.IsConfigured)
        {
            var metadataAddress = options.MetadataAddress!;
            services.AddSingleton<IConfigurationManager<OpenIdConnectConfiguration>>(sp =>
                new ConfigurationManager<OpenIdConnectConfiguration>(
                    metadataAddress,
                    new OpenIdConnectConfigurationRetriever(),
                    new HttpDocumentRetriever(sp.GetRequiredService<IHttpClientFactory>().CreateClient(GatewayTokenValidator.MetadataHttpClientName))
                    {
                        RequireHttps = metadataAddress.StartsWith(Uri.UriSchemeHttps + "://", StringComparison.OrdinalIgnoreCase),
                    }));
        }

        services.AddSingleton(sp => new GatewayTokenValidator(
            sp.GetRequiredService<GatewayTokenValidationOptions>(),
            sp.GetService<IConfigurationManager<OpenIdConnectConfiguration>>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<GatewayTokenValidator>>()));
    }

    private static RateLimitBucketOptions BucketOf(GatewayRateLimitPolicy policy, RateLimitBucketOptions api, RateLimitBucketOptions tiles) =>
        policy == GatewayRateLimitPolicy.Tiles ? tiles : api;

    private static async ValueTask RejectAsync(
        OnRejectedContext context, RateLimitBucketOptions api, RateLimitBucketOptions tiles, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;
        var policy = GatewayRateLimitPolicies.Classify(httpContext.Request.Path);
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var estimate)
            ? estimate
            : BucketOf(policy, api, tiles).ReplenishmentPeriod;
        var retryAfterSeconds = Math.Max(1L, (long)Math.Ceiling(retryAfter.TotalSeconds));

        httpContext.RequestServices.GetRequiredService<GatewayRateLimitMetrics>().RecordRejected(policy);

        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests",
            Detail = string.Create(CultureInfo.InvariantCulture, $"The '{policy.ToName()}' rate limit for this caller is exhausted; retry after {retryAfterSeconds} s."),
        };
        problem.Extensions["code"] = RateLimitExceededCode;
        problem.Extensions["policy"] = policy.ToName();

        await response.WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, ProblemContentType, cancellationToken).ConfigureAwait(false);
    }
}
