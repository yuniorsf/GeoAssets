using System.Diagnostics.Metrics;

namespace GeoAssets.Gateway;

/// <summary>
/// The rate limiter's instruments (XD01-393, XD01-398) on <see cref="GatewayTelemetry.MeterName"/>. Tagged by bounded
/// values only (policy, partition source, validation outcome) — a partition key (organization, subject, client address)
/// is unbounded, so it is never a tag.
/// </summary>
internal sealed class GatewayRateLimitMetrics
{
    /// <summary>Created by, and disposed with, the host's <see cref="IMeterFactory"/>.</summary>
    private readonly Meter _meter;

    private readonly Counter<long> _rateLimited;
    private readonly Counter<long> _partitions;

    public GatewayRateLimitMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        _meter = meterFactory.Create(GatewayTelemetry.MeterName);
        _rateLimited = _meter.CreateCounter<long>(
            GatewayTelemetry.RateLimitedCounterName,
            unit: "{requests}",
            description: "Requests the gateway answered 429 because the caller's token bucket was empty (tag: policy=api|tiles).");
        _partitions = _meter.CreateCounter<long>(
            GatewayTelemetry.RateLimitPartitionCounterName,
            unit: "{requests}",
            description: "Requests by what chose their rate-limit bucket (tags: policy=api|tiles, source=org|sub|ip|unresolved, validation=absent|valid|invalid|unavailable|unvalidated).");
    }

    public void RecordRejected(GatewayRateLimitPolicy policy) =>
        _rateLimited.Add(1, new KeyValuePair<string, object?>(GatewayTelemetry.PolicyTag, policy.ToName()));

    public void RecordPartition(GatewayRateLimitPolicy policy, PartitionCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);

        _partitions.Add(
            1,
            new KeyValuePair<string, object?>(GatewayTelemetry.PolicyTag, policy.ToName()),
            new KeyValuePair<string, object?>(GatewayTelemetry.SourceTag, caller.Source),
            new KeyValuePair<string, object?>(GatewayTelemetry.ValidationTag, caller.Validation.ToName()));
    }
}
