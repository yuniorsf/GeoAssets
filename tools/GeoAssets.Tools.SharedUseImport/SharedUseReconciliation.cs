using GeoAssets.NeutralOperator.Domain.Occupancy;

namespace GeoAssets.Tools.SharedUseImport;

/// <summary>Builds the reconciliation report from a plan and what the run did with each pole (pure).</summary>
internal static class SharedUseReconciliation
{
    /// <exception cref="ArgumentException">A planned pole has no outcome.</exception>
    public static SharedUseImportReport Build(
        SharedUseImportPlan plan,
        IReadOnlyDictionary<string, SharedUsePoleOutcome> outcomes,
        bool dryRun,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(outcomes);

        var poles = plan.Poles.Select(pole => PoleReport(pole, outcomes)).ToList();
        var imported = poles.Where(pole => pole.Outcome is SharedUsePoleOutcome.Created or SharedUsePoleOutcome.Unchanged).ToList();
        var flaggedRows = plan.Poles.SelectMany(pole => pole.FlaggedRows).ToList();

        var totals = new SharedUseImportTotals
        {
            RowsRead = plan.RowsRead,
            RowsAccepted = poles.Sum(pole => pole.RowsImported),
            RowsSkipped = plan.SkippedRows.Count,
            RowsImported = imported.Sum(pole => pole.RowsImported),
            RowsFlagged = flaggedRows.Select(row => row.LegacyRowId).Distinct().Count(),
            EntriesPlanned = poles.Sum(pole => pole.Entries),
            EntriesImported = imported.Sum(pole => pole.Entries),
            RegulatedEntriesPlanned = poles.Sum(pole => pole.RegulatedEntries),
            OverCapacityEntriesPlanned = poles.Sum(pole => pole.OverCapacityEntries),
            Poles = poles.Count,
            PolesImported = imported.Count,
            PolesIrregular = poles.Count(pole => pole.IsIrregular),
            PolesFlagged = poles.Count(pole => pole.Flags.Count > 0 || pole.FlaggedRows.Count > 0),
            SkippedRowsByReason = CountBy(plan.SkippedRows.Select(row => row.Reason)),
            FlaggedRowsByFlag = CountBy(flaggedRows.Select(row => row.Flag)),
            PolesByFlag = CountBy(poles.SelectMany(pole => pole.Flags)),
            PolesByClass = CountBy(poles.Select(pole => pole.Class)),
            PolesByOutcome = CountBy(poles.Select(pole => pole.Outcome)),
        };

        return new SharedUseImportReport(generatedAt, dryRun, plan.OwnerOrganizationId, totals, poles, plan.SkippedRows);
    }

    private static SharedUsePoleReport PoleReport(SharedUsePolePlan pole, IReadOnlyDictionary<string, SharedUsePoleOutcome> outcomes)
    {
        if (!outcomes.TryGetValue(pole.PoleFeatureId, out var outcome))
            throw new ArgumentException($"Pole '{pole.PoleFeatureId}' has no outcome.", nameof(outcomes));

        var occupancy = pole.Occupancy;
        return new SharedUsePoleReport(
            pole.PoleFeatureId,
            outcome,
            OccupancyClassifier.Classify(occupancy),
            occupancy.IsIrregular,
            pole.LegacyRowIds.Count,
            occupancy.Entries.Count,
            occupancy.RegulatedEntries.Count,
            occupancy.OverCapacityEntries.Count,
            occupancy.OccupiedCount,
            pole.LegacyPoleIds,
            pole.Flags,
            pole.FlaggedRows);
    }

    private static SortedDictionary<string, int> CountBy<TEnum>(IEnumerable<TEnum> values)
        where TEnum : struct, Enum
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in Enum.GetNames<TEnum>())
            counts[name] = 0;
        foreach (var value in values)
            counts[value.ToString()]++;
        return counts;
    }
}
