using GeoAssets.Core.Models;

namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// Cross-cutting state for "which <see cref="GeoFeature"/> is currently selected/being edited" —
/// replaces the bespoke <c>SelectedFeatureId</c>/<c>OnFeatureSelected</c>/<c>OnFeatureDeleted</c>
/// parameter threading through <c>AssetList</c> and <c>Index.razor</c>'s own
/// <c>_selectedFeature</c>/<c>_isNewFeature</c> fields, so panel components can become
/// self-sufficient via DI instead of relying on host-supplied parameters (XD01-84).
/// </summary>
public interface IFeatureSelectionState
{
    GeoFeature? Selected { get; }
    bool IsNew { get; }

    /// <summary>Raised whenever <see cref="Selected"/> or <see cref="IsNew"/> changes.</summary>
    event Action? Changed;

    void Select(GeoFeature feature, bool isNew = false);
    void Clear();
}
