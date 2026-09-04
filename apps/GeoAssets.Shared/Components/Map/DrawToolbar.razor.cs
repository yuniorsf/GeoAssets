using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Map;

public partial class DrawToolbar
{
    [Parameter] public string MapDivId { get; set; } = "geoassets-map";
    [Parameter] public EventCallback<GeometryType?> OnDrawModeChanged { get; set; }

    private GeometryType? _active;
    private Guid? _selectedTypeId;
    private bool _paletteOpen;
    private string _search = string.Empty;

    private IReadOnlyList<AssetType> FilteredTypes => FilterAndSort(Repository.GetAssetTypes(), _search);

    private AssetType? SelectedType =>
        _selectedTypeId is { } id ? Repository.GetAssetTypes().FirstOrDefault(t => t.Id == id) : null;

    /// <summary>
    /// Case-insensitive substring match on <see cref="AssetType.Name"/>, alphabetically sorted —
    /// factored out as a pure static method so it's directly unit-testable without rendering
    /// (this repo has no bUnit yet; matches the pattern used by <c>AssetsTable.BuildQuery</c>).
    /// </summary>
    public static IReadOnlyList<AssetType> FilterAndSort(IReadOnlyList<AssetType> types, string search) =>
        [.. types
            .Where(t => string.IsNullOrWhiteSpace(search) || t.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];

    private void TogglePalette()
    {
        _paletteOpen = !_paletteOpen;
        if (!_paletteOpen) _search = string.Empty;
    }

    private void ClosePalette()
    {
        _paletteOpen = false;
        _search = string.Empty;
    }

    private Layer? ResolveStyle(AssetType type) =>
        ResolveStyle(type, Repository.GetLayers(), Repository.GetLayerRules(type.Id));

    /// <summary>
    /// Resolves the style a newly drawn feature of <paramref name="type"/> would get, via the same
    /// tiered resolution <see cref="LayerResolver"/> applies to real features. Tier 1 (a per-feature
    /// <c>LayerId</c> override) never applies pre-draw, so a bare placeholder feature carrying only
    /// <paramref name="type"/>'s id is enough to exercise tiers 2 (matching <c>LayerRule</c>) and 3
    /// (<see cref="AssetType.DefaultLayerId"/>). Static (params instead of reading <c>Repository</c>
    /// directly) so it's directly unit-testable without rendering.
    /// </summary>
    public static Layer? ResolveStyle(AssetType type, IReadOnlyList<Layer> layers, IReadOnlyList<LayerRule> layerRules)
    {
        var placeholder = new GeoFeature { Properties = { AssetTypeId = type.Id.ToString() } };
        return LayerResolver.Resolve(placeholder, type, layers, layerRules);
    }

    /// <summary>
    /// Picking a type-constrained <see cref="AssetType"/> derives the Geoman draw mode automatically
    /// and stashes the type as pending for <see cref="MapContainer.OnFeatureDrawnFromJs"/> to consume.
    /// "Any geometry" types (<see cref="AssetType.AllowedGeometryType"/> is <c>null</c>) are a no-op
    /// here — they're drawn via the 3 raw geometry buttons instead, same as today.
    /// </summary>
    private async Task SelectType(AssetType type)
    {
        if (type.AllowedGeometryType is not { } geometry) return;

        if (_selectedTypeId == type.Id)
        {
            await Cancel();
            return;
        }

        _selectedTypeId = type.Id;
        PendingType.Set(type.Id.ToString());
        _active = geometry;
        ClosePalette();

        await ApplySnapScope(geometry);
        await MapInterop.EnableDrawModeAsync(MapDivId, geometry);
        await OnDrawModeChanged.InvokeAsync(geometry);
    }

    private Task ApplySnapScope(GeometryType geometry)
    {
        var scope = ResolveSnapScope(geometry, Repository.GetAssetTypes());
        return scope is null
            ? MapInterop.ClearSnapTargetScopeAsync(MapDivId)
            : MapInterop.SetSnapTargetLayerAsync(MapDivId, scope);
    }

    /// <summary>
    /// Resolves which AssetTypeIds are valid Geoman snap targets for a type-first draw of
    /// <paramref name="geometry"/> (XD01-119) — <c>null</c> means "unscoped/global", a (possibly
    /// empty) list means "restrict snapping to exactly these types". v1 mirrors XD01-118's own
    /// compatibility heuristic (geometry-type based, not a dedicated AssetType-to-AssetType
    /// registry — none exists) rather than inventing a new one: drawing a Point-type asset scopes
    /// snapping to LineString-type layers only (e.g. a Pole snaps onto Wire, not onto an unrelated
    /// Polygon). Drawing a LineString or Polygon-type asset leaves snapping unscoped — XD01-118
    /// never defined a compatibility heuristic for those shapes either, so no behavior is invented
    /// for them here. Static (params instead of reading <c>Repository</c> directly) so it's
    /// directly unit-testable without rendering, matching <see cref="ResolveStyle(AssetType, IReadOnlyList{Layer}, IReadOnlyList{LayerRule})"/>.
    /// </summary>
    public static IReadOnlyCollection<string>? ResolveSnapScope(GeometryType geometry, IReadOnlyList<AssetType> types) =>
        geometry != GeometryType.Point
            ? null
            : types
                .Where(t => t.AllowedGeometryType == GeometryType.LineString)
                .Select(t => t.Id.ToString())
                .ToList();

    private async Task ToggleMode(GeometryType mode)
    {
        // Only toggle-cancel when the active mode was itself started from a raw-button click —
        // a palette selection that happens to share the same GeometryType (e.g. Pole is a Point
        // type) must still switch definitively into generic/raw drawing, not silently cancel.
        if (_active == mode && _selectedTypeId is null)
        {
            await Cancel();
            return;
        }

        _active = mode;
        _selectedTypeId = null;
        PendingType.Clear();
        await MapInterop.ClearSnapTargetScopeAsync(MapDivId);
        await MapInterop.EnableDrawModeAsync(MapDivId, mode);
        await OnDrawModeChanged.InvokeAsync(mode);
    }

    private async Task Cancel()
    {
        _active = null;
        _selectedTypeId = null;
        PendingType.Clear();
        await MapInterop.ClearSnapTargetScopeAsync(MapDivId);
        await MapInterop.DisableDrawModeAsync(MapDivId);
        await OnDrawModeChanged.InvokeAsync(null);
    }

    public async Task ResetMode()
    {
        _active = null;
        _selectedTypeId = null;
        PendingType.Clear();
        await MapInterop.ClearSnapTargetScopeAsync(MapDivId);
        StateHasChanged();
    }
}
