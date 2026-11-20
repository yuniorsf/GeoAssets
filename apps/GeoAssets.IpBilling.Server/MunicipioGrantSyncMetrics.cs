using System.Diagnostics.Metrics;
using GeoAssets.Infrastructure.Observability;

namespace GeoAssets.IpBilling.Server;

/// <summary>
/// The <c>geoassets.ipbilling.grant_sync</c> counter (XD01-395): one count per grant-sync attempt the dispatcher finished,
/// tagged <c>outcome</c> = <see cref="Synced"/>|<see cref="Retry"/>|<see cref="Failed"/> only — never a município,
/// organization or user id; and the untagged <c>geoassets.ipbilling.grant_sync_pruned</c> counter (XD01-400) of the
/// finished requests the pruner deleted. Created on the <see cref="GeoAssetsMeter.MeterName"/> meter through
/// <see cref="IMeterFactory"/>, so the host's OpenTelemetry pipeline (<c>AddGeoAssetsObservability</c>) exports it with the
/// other domain metrics while the vertical's instrument stays in the vertical.
/// </summary>
internal sealed class MunicipioGrantSyncMetrics
{
    public const string CounterName = "geoassets.ipbilling.grant_sync";

    public const string Synced = "synced";

    public const string Retry = "retry";

    public const string Failed = "failed";

    public const string PrunedCounterName = "geoassets.ipbilling.grant_sync_pruned";

    /// <summary>The factory's meter: the factory owns and disposes it with the container.</summary>
    private readonly Meter _meter;

    private readonly Counter<long> _attempts;

    private readonly Counter<long> _pruned;

    public MunicipioGrantSyncMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        _meter = meterFactory.Create(GeoAssetsMeter.MeterName);
        _attempts = _meter.CreateCounter<long>(
            CounterName,
            unit: "{requests}",
            description: "Município grant-sync attempts finished by the IpBilling dispatcher (tag: outcome=synced|retry|failed).");
        _pruned = _meter.CreateCounter<long>(
            PrunedCounterName,
            unit: "{requests}",
            description: "Finished município grant-sync requests the IpBilling pruner deleted (XD01-400).");
    }

    public void Record(string outcome) =>
        _attempts.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    /// <summary>Counts <paramref name="count"/> pruned requests (untagged); nothing when it is not positive.</summary>
    public void RecordPruned(int count)
    {
        if (count > 0) _pruned.Add(count);
    }
}
