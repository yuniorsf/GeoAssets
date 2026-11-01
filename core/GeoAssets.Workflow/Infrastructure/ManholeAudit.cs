namespace GeoAssets.Workflow.Infrastructure;

/// <summary>
/// One point-in-time audit of a manhole/handhole (<i>Poço de Visita</i>, <i>Caixa de
/// Passagem</i>) — independent of the aerial-pole census (XD01-257/258), its own underground
/// infrastructure concern (XD01-265). Append-only, same convention as
/// <see cref="Contractors.ContractorUsPosting"/>/<see cref="FieldWork.PavingMeasurement"/>: a
/// re-inspection is a new audit row, not an edit to the last one, so the full history stays
/// intact.
///
/// <see cref="BoreOccupancyPercentage"/> and <see cref="SubDucts"/> are deliberately two
/// independently-recorded facts rather than one derived from the other: a 100mm PVC bore's
/// occupancy is a property of the bore itself (how much of its cross-section is used, by
/// whatever means), while the sub-duct inventory is what's actually installed inside it — a
/// bore can be partially occupied by cabling that isn't inside any sub-duct at all.
/// <see cref="SubDuctOccupancyPercentage"/> is a separate, purely computed view over
/// <see cref="SubDucts"/> alone, offered for convenience, never substituted for the recorded
/// <see cref="BoreOccupancyPercentage"/>.
/// </summary>
public sealed class ManholeAudit
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The audited manhole/handhole's own <see cref="Core.Models.GeoFeature.Id"/>.</summary>
    public string ConduitFeatureId { get; init; } = string.Empty;

    public Guid OrganizationId { get; init; } = Guid.Empty;

    /// <summary>0-100: how much of the 100mm PVC bore's cross-section is in use.</summary>
    public double BoreOccupancyPercentage { get; init; }

    public List<SubDuct> SubDucts { get; init; } = [];

    /// <summary>Free text — e.g. "Good", "Corroded", "Cracked, needs replacement". Not an enum: condition reporting varies too much by crew/región to constrain upfront.</summary>
    public string SpliceEnclosureCondition { get; init; } = string.Empty;

    /// <summary>
    /// The NR-33 (confined-space atmospheric safety) checklist submission, validated via
    /// <see cref="ManholeAuditValidator.ValidateNr33Checklist"/> before this audit may be
    /// recorded as complete — the same checklist shape and validation pipeline
    /// <see cref="FieldWork.SafetyChecklistTransitionGuard"/> already uses for field-work orders
    /// (<see cref="FieldWork.SafetyChecklistSchemas.ConfinedSpaceNr33Schema"/>), reused here
    /// rather than duplicated.
    /// </summary>
    public Dictionary<string, string> Nr33ChecklistAttributes { get; init; } = [];

    public string AuditedBy { get; init; } = string.Empty;
    public DateTime AuditedAtUtc { get; init; }

    /// <summary>Purely computed from <see cref="SubDucts"/> — see this type's own doc comment for why it's never conflated with <see cref="BoreOccupancyPercentage"/>.</summary>
    public double SubDuctOccupancyPercentage =>
        SubDucts.Count == 0 ? 0 : 100.0 * SubDucts.Count(d => d.Occupied) / SubDucts.Count;
}
