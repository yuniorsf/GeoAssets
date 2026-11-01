using System.Diagnostics;
using GeoAssets.Core.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// ASP.NET Core middleware that enriches every structured log entry with
/// W3C trace context so that logs and traces can be cross-referenced in
/// the configured OTLP backend (New Relic as of XD01-30) without any
/// manual plumbing.
///
/// Adds to the logging scope:
/// <list type="bullet">
///   <item><term>TraceId</term><description>W3C trace identifier (correlates with distributed traces).</description></item>
///   <item><term>SpanId</term><description>Current span identifier.</description></item>
///   <item><term>TraceFlags</term><description>Sampling flag (01 = sampled).</description></item>
///   <item><term>RequestPath</term><description>HTTP path for quick filtering.</description></item>
///   <item><term>UserId</term><description>Authenticated user identifier (when available).</description></item>
///   <item><term>OrganizationId</term><description><see cref="ITenantContext.CurrentOrganizationId"/>, when the host registers one (XD01-219).</description></item>
/// </list>
///
/// Also tags the current request's <see cref="Activity"/> with
/// <c>organization.id</c> (XD01-219's "cardinality-safe" requirement) — a span attribute, not a
/// metric dimension. Trace backends index per-span/per-trace, so one series per distinct
/// organization costs nothing there; a metric *series* is identified by its full tag set, so
/// adding a per-tenant tag there would multiply every metric's series count by the tenant count
/// and keep growing as tenants are onboarded. <see cref="GeoAssetsMeter"/> must never add
/// <c>organization.id</c>/tenant id as an instrument tag for this reason — per-tenant metric
/// drill-down goes through trace search on this span attribute instead, not a metric dimension.
///
/// <paramref name="tenantContext"/> is optional: not every host registers
/// <see cref="ITenantContext"/> (e.g. <c>GeoAssets.IpBilling.Server</c> doesn't), and this
/// middleware must keep working — just without the extra field — when it isn't.
///
/// Register in <c>Program.cs</c> <b>after</b> <c>UseAuthentication</c>:
/// <code>
/// app.UseMiddleware&lt;TelemetryEnrichmentMiddleware&gt;();
/// </code>
/// </summary>
public sealed class TelemetryEnrichmentMiddleware(
    RequestDelegate next,
    ILogger<TelemetryEnrichmentMiddleware> logger,
    ITenantContext? tenantContext = null)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var activity = Activity.Current;
        activity?.SetTag("organization.id", tenantContext?.CurrentOrganizationId.ToString());

        using (logger.BeginScope(BuildScope(context, activity, tenantContext)))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    private static Dictionary<string, object?> BuildScope(HttpContext ctx, Activity? activity, ITenantContext? tenantContext)
    {
        var scope = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["RequestPath"] = ctx.Request.Path.Value,
            ["RequestMethod"] = ctx.Request.Method,
        };

        if (activity is not null)
        {
            scope["TraceId"] = activity.TraceId.ToString();
            scope["SpanId"] = activity.SpanId.ToString();
            scope["TraceFlags"] = activity.ActivityTraceFlags.ToString();
        }

        if (ctx.User.Identity?.IsAuthenticated == true)
        {
            scope["UserId"] = ctx.User.FindFirst("oid")?.Value
                           ?? ctx.User.FindFirst("sub")?.Value
                           ?? ctx.User.Identity.Name;
        }

        if (tenantContext is not null)
        {
            scope["OrganizationId"] = tenantContext.CurrentOrganizationId;
        }

        return scope;
    }
}
