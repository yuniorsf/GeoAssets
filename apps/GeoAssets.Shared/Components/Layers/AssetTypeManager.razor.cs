using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Shared.Components.Layers;

public partial class AssetTypeManager
{
    private string _activeTab = "types";
    private Guid? _expandedTypeId;

    private bool _addingNew;
    private string _newTypeName = string.Empty;
    private string _newTypeColor = "#89b4fa";
    private GeometryType? _newTypeAllowedGeometry;
    private Guid? _newTypeDefaultLayerId;
    private string _newTypeAttributesSchemaJson = string.Empty;
    private string? _newTypeSchemaError;
    private string _newTypeMinZoom = string.Empty;
    private string _newTypeMaxZoom = string.Empty;
    private AssetTypeZoomRangeInputResult? _newTypeZoomProblem;

    // The user's layer toggles live in MapLayerDisplayService, not here: this panel is closed and
    // reopened freely, and the map keeps a switched-off type hidden across re-renders (XD01-311).
    private bool IsVisible(AssetType type) => LayerDisplay.IsVisible(MapContext.MapDivId, type.Id.ToString());

    private void ShowTypesTab() => _activeTab = "types";
    private void ShowStylesTab() => _activeTab = "styles";

    private Task ToggleLayer(AssetType type, bool visible) =>
        LayerDisplay.SetVisibleAsync(MapContext.MapDivId, type.Id.ToString(), visible);

    private string ZoomRangeLabel(AssetType type) =>
        L.GetString(
            AssetTypeZoomRangeInput.SummaryKey(type.MinZoom, type.MaxZoom),
            AssetTypeZoomRangeInput.SummaryArguments(type.MinZoom, type.MaxZoom));

    private string? ZoomRangeError => _newTypeZoomProblem?.Error is { } error
        ? L.GetString(AssetTypeZoomRangeInput.ErrorKey(error), AssetTypeZoomRange.LowestZoom, AssetTypeZoomRange.HighestZoom)
        : null;

    // Rendered only when true, so a valid field carries no aria-invalid attribute at all.
    private static string? AriaInvalid(bool invalid) => invalid ? "true" : null;

    private void ToggleExpand(Guid typeId) =>
        _expandedTypeId = _expandedTypeId == typeId ? null : typeId;

    private string GeometryTypeLabel(GeometryType type) => type switch
    {
        GeometryType.Point => L["map.draw.point"],
        GeometryType.LineString => L["map.draw.line"],
        GeometryType.Polygon => L["map.draw.polygon"],
        _ => type.ToString()
    };

    /// <summary>Layers matching <paramref name="geometryType"/>, or all layers when unset (any geometry).</summary>
    private IReadOnlyList<Layer> LayersForGeometry(GeometryType? geometryType) =>
        geometryType is null
            ? [.. Repository.GetLayers()]
            : [.. Repository.GetLayers().Where(l => l.GeometryType == geometryType)];

    private void OnNewTypeAllowedGeometryChanged(string? value)
    {
        _newTypeAllowedGeometry = string.IsNullOrEmpty(value) ? null : Enum.Parse<GeometryType>(value);
        // The previously picked default layer may no longer match the new geometry filter.
        _newTypeDefaultLayerId = null;
    }

    private void OnNewTypeDefaultLayerChanged(string? value) =>
        _newTypeDefaultLayerId = string.IsNullOrEmpty(value) ? null : Guid.Parse(value);

    private async Task SaveNewType()
    {
        if (string.IsNullOrWhiteSpace(_newTypeName)) return;

        _newTypeSchemaError = null;
        var zoom = AssetTypeZoomRangeInput.Parse(_newTypeMinZoom, _newTypeMaxZoom);
        _newTypeZoomProblem = zoom.IsValid ? null : zoom;
        if (!zoom.IsValid) return;

        var schemaJson = _newTypeAttributesSchemaJson.Trim();
        if (schemaJson.Length > 0)
        {
            try { JsonDocument.Parse(schemaJson); }
            catch (JsonException)
            {
                _newTypeSchemaError = L["map.layers.invalidJson"];
                return;
            }
        }

        var newType = new AssetType
        {
            Name = _newTypeName.Trim(),
            Color = _newTypeColor,
            AllowedGeometryType = _newTypeAllowedGeometry,
            DefaultLayerId = _newTypeDefaultLayerId,
            AttributesSchemaJson = schemaJson.Length > 0 ? schemaJson : null,
            MinZoom = zoom.MinZoom,
            MaxZoom = zoom.MaxZoom
        };
        Repository.AddAssetType(newType);
        ResetNewTypeForm();

        // Adding a type raises no repository event, so the map is told about its range here.
        await SyncZoomRangesAsync();
    }

    private Task SyncZoomRangesAsync() =>
        LayerDisplay.SyncZoomRangesAsync(MapContext.MapDivId, Repository.GetAssetTypes());

    private void ResetNewTypeForm()
    {
        _newTypeName = string.Empty;
        _newTypeColor = "#89b4fa";
        _newTypeAllowedGeometry = null;
        _newTypeDefaultLayerId = null;
        _newTypeAttributesSchemaJson = string.Empty;
        _newTypeSchemaError = null;
        _newTypeMinZoom = string.Empty;
        _newTypeMaxZoom = string.Empty;
        _newTypeZoomProblem = null;
        _addingNew = false;
    }

    private async Task DeleteType(AssetType type)
    {
        Repository.DeleteAssetType(type.Id);
        if (_expandedTypeId == type.Id) _expandedTypeId = null;

        // Its features fall back to "no type": switch them back on and drop the type's range.
        if (!IsVisible(type)) await ToggleLayer(type, visible: true);
        await SyncZoomRangesAsync();
    }
}
