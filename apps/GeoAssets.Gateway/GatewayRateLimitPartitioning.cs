using GeoAssets.Identity.Authentication;

namespace GeoAssets.Gateway;

/// <summary>
/// Resolves each request's rate-limit caller before the rate limiter runs (XD01-398). The limiter's partitioner is
/// synchronous and token validation is not (the signing keys come from a cached metadata document), so this middleware —
/// placed right before <c>UseRateLimiter()</c> — validates the bearer token (<see cref="GatewayTokenValidator"/>),
/// resolves the <see cref="PartitionCaller"/> (<see cref="RateLimitPartitionKeyResolver"/>) and leaves it in
/// <see cref="HttpContext.Items"/> under <see cref="CallerItemKey"/> for the partitioner; it also counts it on
/// <see cref="GatewayTelemetry.RateLimitPartitionCounterName"/>. It never rejects a request. <c>/healthz</c> is skipped,
/// like the limiter skips it.
/// </summary>
internal static partial class GatewayRateLimitPartitioning
{
    /// <summary>The <see cref="HttpContext.Items"/> key of the request's <see cref="PartitionCaller"/>.</summary>
    public const string CallerItemKey = "GeoAssets.Gateway.RateLimitCaller";

    private const string HealthPath = "/healthz";

    public static IApplicationBuilder UseRateLimitPartitioning(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var services = app.ApplicationServices;
        var validator = services.GetRequiredService<GatewayTokenValidator>();
        var claimMapping = services.GetRequiredService<ClaimMapping>();
        var metrics = services.GetRequiredService<GatewayRateLimitMetrics>();

        if (!validator.IsConfigured)
            LogValidationNotConfigured(services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(GatewayRateLimitPartitioning)));

        return app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase))
            {
                var validation = await validator
                    .ValidateAsync(RateLimitPartitionKeyResolver.ReadBearerToken(context.Request), context.RequestAborted)
                    .ConfigureAwait(false);
                var caller = RateLimitPartitionKeyResolver.Resolve(validation, claimMapping, context.Connection.RemoteIpAddress);
                context.Items[CallerItemKey] = caller;
                metrics.RecordPartition(GatewayRateLimitPolicies.Classify(context.Request.Path), caller);
            }

            await next(context).ConfigureAwait(false);
        });
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Token validation is not configured (AzureAdCiam:Instance and AzureAdCiam:TenantId, or AzureAdCiam:MetadataAddress): rate limits key on unvalidated token claims, so a forged token can name another caller's subject or organization and spend that caller's bucket.")]
    private static partial void LogValidationNotConfigured(ILogger logger);
}
