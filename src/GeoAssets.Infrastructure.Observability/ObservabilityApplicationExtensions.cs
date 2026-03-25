using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;

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
    /// </summary>
    public static IApplicationBuilder UseGeoAssetsObservability(this IApplicationBuilder app)
    {
        app.UseMiddleware<TelemetryEnrichmentMiddleware>();

        app.UseHealthChecks("/healthz", new HealthCheckOptions
        {
            ResponseWriter = async (ctx, report) =>
            {
                ctx.Response.ContentType = "application/json";
                var status = report.Status.ToString().ToLowerInvariant();
                await ctx.Response.WriteAsync($"{{\"status\":\"{status}\"}}");
            }
        });

        return app;
    }
}
