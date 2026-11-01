using GeoAssets.Workflow.FieldWork;

namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// XD01-246's "configurable per-item gating rule" (AC #3): which <see cref="PhotoGatingStage"/>s
/// a BoQ catalog item (<see cref="ItemCode"/>, matching <see cref="ServiceOrderBoqAttributes"/>'s
/// <c>itemCode</c>) requires evidence for. An item with no registered rule requires nothing — the
/// same "no rule = unrestricted" convention <see cref="SafetyChecklistTransitionGuard"/> already
/// uses for order types with no attribute schema.
/// </summary>
public sealed record PhotoGatingRule(string ItemCode, IReadOnlySet<PhotoGatingStage> RequiredStages);
