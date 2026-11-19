namespace GeoAssets.Workflow.Targets;

/// <summary>
/// What a ledger reservation would write (<see cref="OrderTargetSelectionService.PlanLedgerReservationAsync"/>), before
/// anything is written.
/// </summary>
/// <param name="Targets">The targets to insert: the kept features, none of which the order already holds.</param>
/// <param name="Result">The counts of the selection; <see cref="TargetSelectionResult.Added"/> is 0 until the targets are written.</param>
/// <param name="Conflicts">The holders blocking the reservation; empty when it may proceed.</param>
public sealed record TargetLedgerReservation(
    IReadOnlyList<ServiceOrderTarget> Targets,
    TargetSelectionResult Result,
    IReadOnlyList<TargetLedgerConflict> Conflicts);
