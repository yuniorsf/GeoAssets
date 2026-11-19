namespace GeoAssets.Shared.Components.Map;

/// <summary>Where the context menu's feature-action load stands (XD01-370).</summary>
public enum FeatureActionsState
{
    Idle,
    Loading,
    Loaded,
    Failed,
}
