using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// ASP.NET Core middleware that enriches every structured log entry with
/// W3C trace context so that logs and traces can be cross-referenced in
/// Azure Monitor / Application Insights without any manual plumbing.
///
/// Adds to the logging scope:
/// <list type="bullet">
///   <item><term>TraceId</term><description>W3C trace identifier (correlates with distributed traces).</description></item>
///   <item><term>SpanId</term><description>Current span identifier.</description></item>
///   <item><term>TraceFlags</term><description>Sampling flag (01 = sampled).</description></item>
///   <item><term>RequestPath</term><description>HTTP path for quick filtering.</description></item>
///   <item><term>UserId</term><description>Authenticated user identifier (when available).</description></item>
/// </list>
///
/// Register in <c>Program.cs</c> <b>after</b> <c>UseAuthentication</c>:
/// <code>
/// app.UseMiddleware&lt;TelemetryEnrichmentMiddleware&gt;();
/// </code>
/// </summary>
public sealed class TelemetryEnrichmentMiddleware(
    RequestDelegate              next,
    ILogger<TelemetryEnrichmentMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var activity = Activity.Current;

        using (logger.BeginScope(BuildScope(context, activity)))
        {
            await next(context);
        }
    }

    private static Dictionary<string, object?> BuildScope(HttpContext ctx, Activity? activity)
    {
        var scope = new Dictionary<string, object?>
        {
            ["RequestPath"] = ctx.Request.Path.Value,
            ["RequestMethod"] = ctx.Request.Method,
        };

        if (activity is not null)
        {
            scope["TraceId"]    = activity.TraceId.ToString();
            scope["SpanId"]     = activity.SpanId.ToString();
            scope["TraceFlags"] = activity.ActivityTraceFlags.ToString();
        }

        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            scope["UserId"] = ctx.User.FindFirst("oid")?.Value
                           ?? ctx.User.FindFirst("sub")?.Value
                           ?? ctx.User.Identity.Name;
        }

        return scope;
    }
}
