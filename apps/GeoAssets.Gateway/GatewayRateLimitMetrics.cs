using System.Diagnostics.Metrics;

namespace GeoAssets.Gateway;

/// <summary>
/// The rate limiter's instruments (XD01-393) on <see cref="GatewayTelemetry.MeterName"/>. Tagged by policy only — a
/// partition key (organization, subject, client address) is unbounded, so it is never a tag.
/// </summary>
internal sealed class GatewayRateLimitMetrics
{
    /// <summary>Created by, and disposed with, the host's <see cref="IMeterFactory"/>.</summary>
    private readonly Meter _meter;

    private readonly Counter<long> _rateLimited;

    public GatewayRateLimitMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        _meter = meterFactory.Create(GatewayTelemetry.MeterName);
        _rateLimited = _meter.CreateCounter<long>(
            GatewayTelemetry.RateLimitedCounterName,
            unit: "{requests}",
            description: "Requests the gateway answered 429 because the caller's token bucket was empty (tag: policy=api|tiles).");
    }

    public void RecordRejected(GatewayRateLimitPolicy policy) =>
        _rateLimited.Add(1, new KeyValuePair<string, object?>(GatewayTelemetry.PolicyTag, policy.ToName()));
}
