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

        await MapInterop.EnableDrawModeAsync(MapDivId, geometry);
        await OnDrawModeChanged.InvokeAsync(geometry);
    }

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
        await MapInterop.EnableDrawModeAsync(MapDivId, mode);
        await OnDrawModeChanged.InvokeAsync(mode);
    }

    private async Task Cancel()
    {
        _active = null;
        _selectedTypeId = null;
        PendingType.Clear();
        await MapInterop.DisableDrawModeAsync(MapDivId);
        await OnDrawModeChanged.InvokeAsync(null);
    }

    public void ResetMode()
    {
        _active = null;
        _selectedTypeId = null;
        PendingType.Clear();
        StateHasChanged();
    }
}
