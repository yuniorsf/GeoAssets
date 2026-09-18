using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Shared.Components.Assets;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Map;

public partial class MapWorkspace
{
    private const string _mapDivId = "geoassets-map";

    /// <summary>Whether this workspace's host route is not the active one (e.g. a persistent layout navigated elsewhere) — hides the container via CSS instead of unmounting it, so the map survives navigation.</summary>
    [Parameter] public bool Hidden { get; set; }

    private MapContainer?    _mapContainer;
    private DrawToolbar?     _drawToolbar;
    private AssetForm?       _assetForm;
    private CandidatePicker? _candidatePicker;

    private bool _wasHidden;

    // ─── Candidate picker state (XD01-152) ─────────────────────────────────

    private bool _pickerVisible;
    private string? _pickerTargetFeatureId;
    private IReadOnlyList<GeoFeature> _pickerCandidates = [];

    // ─── Context menu state ────────────────────────────────────────────────

    private bool   _contextVisible;
    private string _contextFeatureId = string.Empty;
    private double _contextX;
    private double _contextY;

    private bool   _showDeleteConfirm;
    private string _pendingDeleteId   = string.Empty;
    private string _pendingDeleteName = string.Empty;

    // ─── Init ─────────────────────────────────────────────────────────────

    protected override void OnInitialized()
    {
        base.OnInitialized();
        Selection.Changed += OnSelectionChanged;
    }

    protected override async Task OnInitializedAsync()
    {
        await AssetSvc.InitializeAsync();
    }

    // Wakes Leaflet up after the container goes from CSS-hidden back to visible — Leaflet
    // doesn't notice a display:none -> display:block container resize on its own. Must run
    // after the DOM patch (OnAfterRenderAsync), not OnParametersSet, since invalidateSize()
    // needs the container's post-patch layout dimensions.
    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        var justShown = _wasHidden && !Hidden;
        _wasHidden = Hidden;
        return justShown ? MapInterop.InvalidateSizeAsync(_mapDivId) : Task.CompletedTask;
    }

    // ─── Selection ────────────────────────────────────────────────────────

    // Reflects any change to Selection, no matter which component made it (this page's own
    // draw/click handlers below, or AssetList's row click / delete flow) — a shared injected
    // service mutated elsewhere doesn't otherwise trigger this component's re-render. Also
    // preserves today's exact pan-on-select-but-not-on-draw behavior via IsNew: a freshly
    // drawn feature (IsNew true) is already visible where it was just drawn, so panning to it
    // would be pointless motion; a list/map-click selection (IsNew false) pans to bring an
    // already-existing feature into view.
    private void OnSelectionChanged() => InvokeAsync(async () =>
    {
        // Selecting a different feature (map click, AssetList row, etc.) while the candidate
        // picker is open is an implicit abandon — same silent no-op as declining/dismissing.
        if (_pickerVisible) ClosePicker();

        StateHasChanged();
        if (Selection.Selected is { } feature && !Selection.IsNew)
            await (_mapContainer?.PanToFeatureAsync(feature.Id) ?? Task.CompletedTask);
    });

    // ─── Draw events ──────────────────────────────────────────────────────

    private void OnDrawModeChanged(GeometryType? mode)
    {
        if (mode.HasValue) ClosePanel();
    }

    private async Task OnFeatureDrawn(GeoFeature feature)
    {
        Selection.Select(feature, isNew: true);
        if (_drawToolbar is not null) await _drawToolbar.ResetMode();
    }

    private void OnFeatureEdited(GeoFeature feature) => StateHasChanged();

    // ─── Selection / click ────────────────────────────────────────────────

    private void OnFeatureSelected(GeoFeature feature) => Selection.Select(feature, isNew: false);

    private void OnFeatureClicked(string featureId)
    {
        // While the picker is open, clicking its target asset cycles candidates instead of
        // reopening the properties panel for it.
        if (_pickerVisible && featureId == _pickerTargetFeatureId)
        {
            _candidatePicker?.CycleNext();
            return;
        }

        var feature = Repository.GetById(featureId);
        if (feature is not null)
            OnFeatureSelected(feature);
    }

    // ─── Multi-candidate "Connect to…" picker (XD01-152) ───────────────────

    private void OnMultiCandidateFound((GeoFeature Feature, IReadOnlyList<GeoFeature> Candidates) found)
    {
        _pickerTargetFeatureId = found.Feature.Id;
        _pickerCandidates      = found.Candidates;
        _pickerVisible         = true;
        StateHasChanged();
    }

    private void OnCandidatesLinked(IReadOnlyList<GeoFeature> chosen)
    {
        if (_pickerTargetFeatureId is { } targetId && Repository.GetById(targetId) is { } feature)
        {
            foreach (var candidate in chosen)
                feature.Topology.Add(new TopoEdge { TargetId = candidate.Id, Kind = "connected-to", Weight = 1.0 });
            Repository.Update(feature);
        }
        ClosePicker();
    }

    private void CancelPicker() => ClosePicker();

    private void ClosePicker()
    {
        if (_candidatePicker?.CurrentHighlighted is { } highlighted)
            _ = MapInterop.ClearHighlightAsync(_mapDivId, highlighted.Id);

        _pickerVisible          = false;
        _pickerTargetFeatureId  = null;
        _pickerCandidates       = [];
        StateHasChanged();
    }

    // ─── Context menu ─────────────────────────────────────────────────────

    private void OnFeatureContextMenu((string FeatureId, double X, double Y) ctx)
    {
        _contextFeatureId = ctx.FeatureId;
        _contextX         = ctx.X;
        _contextY         = ctx.Y;
        _contextVisible   = true;
        StateHasChanged();
    }

    private void CloseContextMenu()
    {
        _contextVisible = false;
        StateHasChanged();
    }

    private void ContextMenuEdit()
    {
        var feature = Repository.GetById(_contextFeatureId);
        if (feature is not null)
            OnFeatureSelected(feature);
    }

    private async Task ContextMenuSave()
    {
        if (_assetForm is not null)
            await _assetForm.SaveAsync();
    }

    private void ContextMenuDelete()
    {
        var feature = Repository.GetById(_contextFeatureId);
        if (feature is null) return;
        _pendingDeleteId   = feature.Id;
        _pendingDeleteName = string.IsNullOrEmpty(feature.Properties.Name)
            ? L["assets.noName"]
            : feature.Properties.Name;
        _showDeleteConfirm = true;
        StateHasChanged();
    }

    private void ConfirmContextDelete()
    {
        if (!string.IsNullOrEmpty(_pendingDeleteId))
        {
            Repository.Delete(_pendingDeleteId);
            if (Selection.Selected?.Id == _pendingDeleteId)
                ClosePanel();
        }
        _pendingDeleteId   = string.Empty;
        _pendingDeleteName = string.Empty;
        _showDeleteConfirm = false;
        StateHasChanged();
    }

    // ─── Form actions ─────────────────────────────────────────────────────

    private void OnFeatureSaved(GeoFeature _) => ClosePanel();
    private void ClosePanel() => Selection.Clear();

    public async ValueTask DisposeAsync()
    {
        Selection.Changed -= OnSelectionChanged;
        await AssetSvc.DisposeAsync();
    }
}
