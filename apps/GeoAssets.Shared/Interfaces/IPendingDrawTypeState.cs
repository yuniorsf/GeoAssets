namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// Cross-cutting state for "which <c>AssetType</c> the user picked from
/// <c>DrawToolbar</c>'s type palette before drawing the next feature" (XD01-117) — set when a
/// type-constrained row is selected, consumed (and cleared) by
/// <c>MapContainer.OnFeatureDrawnFromJs</c> once the feature lands. <c>null</c> means no
/// type-first draw is pending, e.g. the 3 raw geometry buttons were used instead — callers fall
/// back to geometry-based <c>AssetType</c> inference in that case. Mirrors
/// <see cref="IFeatureSelectionState"/>/<see cref="ICurrentMapContext"/>'s "self-sufficient via
/// DI instead of parameter-threading" pattern (XD01-84).
/// </summary>
public interface IPendingDrawTypeState
{
    string? AssetTypeId { get; }

    void Set(string assetTypeId);
    void Clear();
}
