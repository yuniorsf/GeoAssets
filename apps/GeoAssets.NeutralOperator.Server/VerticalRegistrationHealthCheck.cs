using System.Globalization;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// The <c>vertical-registration</c> health check (XD01-382): <see cref="HealthStatus.Healthy"/> once the kernel knows this
/// vertical's order types and permissions; <see cref="HealthStatus.Degraded"/> while the registration is pending or failing
/// — so <c>/healthz</c> (liveness) still answers 200 and a kernel outage never restarts this host, while <c>/readyz</c>
/// (tagged <see cref="ReadinessTag"/>) answers 503 until it is registered.
/// </summary>
internal sealed class VerticalRegistrationHealthCheck(VerticalRegistrationState state) : IHealthCheck
{
    public const string Name = "vertical-registration";

    /// <summary>The tag of the checks <c>/readyz</c> evaluates.</summary>
    public const string ReadinessTag = "ready";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var current = state.Current;
        var data = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["status"] = current.Status.ToString(),
            ["consecutiveFailures"] = current.ConsecutiveFailures,
        };
        if (current.RegisteredAt is { } at) data["registeredAt"] = at.ToString("O", CultureInfo.InvariantCulture);

        return Task.FromResult(current.Status == VerticalRegistrationStatus.Registered
            ? HealthCheckResult.Healthy("Registered with the kernel.", data)
            : HealthCheckResult.Degraded(
                current.LastError is { } error ? $"Registration with the kernel is failing: {error}" : "Registration with the kernel is pending.",
                data: data));
    }
}
