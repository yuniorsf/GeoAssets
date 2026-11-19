using GeoAssets.NeutralOperator.Domain.Occupancy;

namespace GeoAssets.Tools.SharedUseImport;

/// <summary>
/// Writes a <see cref="SharedUseImportPlan"/> through the pole occupancy ports (XD01-314) and
/// reports what it did. Idempotent: a pole that already holds exactly the planned occupancy is
/// left alone (no new version), and a pole holding anything else is never overwritten, so running
/// the same input again changes nothing and reports the same reconciliation.
///
/// Each pole is its own write: a run stopped half way leaves whole poles behind, and the next run
/// picks up where it stopped.
/// </summary>
internal sealed class SharedUseImporter(IPoleOccupancyReader reader, IPoleOccupancyWriter writer, TimeProvider timeProvider)
{
    /// <summary>How many poles are read from the store per call.</summary>
    public const int ReadBatchSize = 500;

    private readonly IPoleOccupancyReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    private readonly IPoleOccupancyWriter _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    private readonly TimeProvider _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    /// <summary>Applies <paramref name="plan"/> (or, when <paramref name="dryRun"/>, only compares it with the store).</summary>
    public async Task<SharedUseImportReport> ImportAsync(SharedUseImportPlan plan, bool dryRun, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ct.ThrowIfCancellationRequested();

        var outcomes = new Dictionary<string, SharedUsePoleOutcome>(StringComparer.Ordinal);
        foreach (var batch in plan.Poles.Chunk(ReadBatchSize))
        {
            var stored = (await _reader.GetForPolesAsync([.. batch.Select(pole => pole.PoleFeatureId)], ct).ConfigureAwait(false))
                .ToDictionary(pole => pole.PoleFeatureId, StringComparer.Ordinal);

            foreach (var pole in batch)
            {
                outcomes[pole.PoleFeatureId] = stored.TryGetValue(pole.PoleFeatureId, out var current)
                    ? Compare(current, pole)
                    : await CreateAsync(pole, dryRun, ct).ConfigureAwait(false);
            }
        }

        return SharedUseReconciliation.Build(plan, outcomes, dryRun, _timeProvider.GetUtcNow());
    }

    private static SharedUsePoleOutcome Compare(PoleOccupancy current, SharedUsePolePlan pole) =>
        current.Entries.SequenceEqual(pole.Occupancy.Entries)
            ? SharedUsePoleOutcome.Unchanged
            : SharedUsePoleOutcome.ExistingOccupancyDiffers;

    private async Task<SharedUsePoleOutcome> CreateAsync(SharedUsePolePlan pole, bool dryRun, CancellationToken ct)
    {
        if (dryRun) return SharedUsePoleOutcome.WouldCreate;

        try
        {
            await _writer.ReplaceEntriesAsync(pole.PoleFeatureId, PoleOccupancy.UnsavedVersion, pole.Occupancy.Entries, ct)
                .ConfigureAwait(false);
            return SharedUsePoleOutcome.Created;
        }
        catch (OccupancyConflictException)
        {
            return SharedUsePoleOutcome.ConcurrentWrite;
        }
    }
}
