using GeoAssets.Core.Isp.NeutralOperator.Occupancy;

namespace GeoAssets.Tools.SharedUseImport;

/// <summary>One pole of the reconciliation report.</summary>
/// <param name="PoleFeatureId">The pole feature id.</param>
/// <param name="Outcome">What the import did with it.</param>
/// <param name="Class">The planned occupancy's class (<see cref="OccupancyClassifier"/>).</param>
/// <param name="IsIrregular">Over capacity, an unidentified occupant, or both.</param>
/// <param name="RowsImported">Legacy rows planned onto the pole.</param>
/// <param name="Entries">Planned entries.</param>
/// <param name="RegulatedEntries">Planned entries on slots 1..6.</param>
/// <param name="OverCapacityEntries">Planned entries beyond the sixth (no slot).</param>
/// <param name="OccupiedCount">Planned occupied points.</param>
/// <param name="LegacyPoleIds">The legacy poles combined into this one.</param>
/// <param name="Flags">Pole-level flags.</param>
/// <param name="FlaggedRows">Row-level flags on the pole's rows.</param>
internal sealed record SharedUsePoleReport(
    string PoleFeatureId,
    SharedUsePoleOutcome Outcome,
    OccupancyClass Class,
    bool IsIrregular,
    int RowsImported,
    int Entries,
    int RegulatedEntries,
    int OverCapacityEntries,
    int OccupiedCount,
    IReadOnlyList<int> LegacyPoleIds,
    IReadOnlyList<SharedUsePoleFlag> Flags,
    IReadOnlyList<FlaggedSharedUseRow> FlaggedRows);
