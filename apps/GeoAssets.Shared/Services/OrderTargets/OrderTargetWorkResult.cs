using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>
/// What a save of the per-target work editor left stored (XD01-313): <paramref name="Target"/> is
/// the target as stored now; <paramref name="Saved"/> is true when that is this save's own
/// write, false when someone else's change was read back after a conflict.
/// </summary>
public sealed record OrderTargetWorkResult(ServiceOrderTarget Target, bool Saved);
