namespace GeoAssets.Tools.SharedUseImport;

/// <summary>
/// The report's totals. Rows: <c>RowsRead = RowsAccepted + RowsSkipped</c>; an accepted row is
/// planned onto a pole, and imported once that pole holds the planned occupancy
/// (<see cref="SharedUsePoleOutcome.Created"/> or <see cref="SharedUsePoleOutcome.Unchanged"/>).
/// Every count keyed by a reason, flag, class or outcome lists all of its values, zeros included.
/// </summary>
internal sealed record SharedUseImportTotals
{
    public required int RowsRead { get; init; }
    public required int RowsAccepted { get; init; }
    public required int RowsSkipped { get; init; }
    public required int RowsImported { get; init; }
    public required int RowsFlagged { get; init; }
    public required int EntriesPlanned { get; init; }
    public required int EntriesImported { get; init; }
    public required int RegulatedEntriesPlanned { get; init; }
    public required int OverCapacityEntriesPlanned { get; init; }
    public required int Poles { get; init; }
    public required int PolesImported { get; init; }
    public required int PolesIrregular { get; init; }
    public required int PolesFlagged { get; init; }
    public required IReadOnlyDictionary<string, int> SkippedRowsByReason { get; init; }
    public required IReadOnlyDictionary<string, int> FlaggedRowsByFlag { get; init; }
    public required IReadOnlyDictionary<string, int> PolesByFlag { get; init; }
    public required IReadOnlyDictionary<string, int> PolesByClass { get; init; }
    public required IReadOnlyDictionary<string, int> PolesByOutcome { get; init; }
}
