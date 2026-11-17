using System.Diagnostics.Metrics;

namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// The asset mutation batch instruments of <see cref="GeoAssetsMeter"/> (XD01-337), created on
/// that meter. None is ever tagged with a batch, organization, order or feature id.
/// </summary>
internal sealed class AssetMutationBatchInstruments(Meter meter)
{
    /// <summary>Attempts to apply an asset mutation batch to the catalog (tag: outcome).</summary>
    public Counter<long> Batches { get; } = meter.CreateCounter<long>(
        "geoassets.catalog.mutation_batches",
        unit: "{batches}",
        description: "Attempts to apply an asset mutation batch to the catalog (tag: outcome=ok|rejected|cancelled|error).");

    /// <summary>Duration of one attempt (tag: outcome).</summary>
    public Histogram<double> DurationMs { get; } = meter.CreateHistogram<double>(
        "geoassets.catalog.mutation_batch.duration",
        unit: "ms",
        description: "Duration of one attempt to apply an asset mutation batch to the catalog (tag: outcome=ok|rejected|cancelled|error).");
}
