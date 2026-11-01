using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// <see cref="IApplicationBuilder"/> extensions for the observability pipeline.
/// </summary>
public static class ObservabilityApplicationExtensions
{
    /// <summary>
    /// Registers:
    /// <list type="bullet">
    ///   <item><see cref="TelemetryEnrichmentMiddleware"/> — injects TraceId/SpanId into every log scope.</item>
    ///   <item>A <c>/healthz</c> liveness endpoint — excluded from traces to avoid noise.</item>
    /// </list>
    ///
    /// Call after <c>UseAuthentication()</c> / <c>UseAuthorization()</c>:
    /// <code>
    /// app.UseGeoAssetsObservability();
    /// </code>
    ///
    /// When <paramref name="app"/> is also an <see cref="IEndpointRouteBuilder"/> (true for every
    /// real host — <c>WebApplication</c> implements both), <c>/healthz</c> is registered via
    /// <see cref="HealthCheckEndpointRouteBuilderExtensions.MapHealthChecks"/> and marked
    /// <c>AllowAnonymous()</c>. This matters because every host using
    /// <c>GeoAssetsAuthenticationExtensions.AddGeoAssetsAuthentication</c> sets a
    /// <c>FallbackPolicy</c> requiring an authenticated user on every endpoint with no
    /// endpoint-specific policy — without this, Kubernetes liveness/readiness probes (which carry
    /// no bearer token) get 401 instead of a health status, and the pod never becomes Ready. Falls
    /// back to the old terminal-middleware <c>UseHealthChecks</c> for a bare
    /// <see cref="IApplicationBuilder"/> pipeline with no endpoint routing (e.g. this project's own
    /// tests), where there is no fallback-policy concern to route around.
    /// </summary>
    public static IApplicationBuilder UseGeoAssetsObservability(this IApplicationBuilder app)
    {
        app.UseMiddleware<TelemetryEnrichmentMiddleware>();

        var healthCheckOptions = new HealthCheckOptions
        {
            ResponseWriter = async (ctx, report) =>
            {
                ctx.Response.ContentType = "application/json";
                var status = report.Status.ToString().ToLowerInvariant();
                await ctx.Response.WriteAsync($"{{\"status\":\"{status}\"}}").ConfigureAwait(false);
            }
        };

        if (app is IEndpointRouteBuilder endpoints)
            endpoints.MapHealthChecks("/healthz", healthCheckOptions).AllowAnonymous();
        else
            app.UseHealthChecks("/healthz", healthCheckOptions);

        return app;
    }
}
