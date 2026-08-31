using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Shared.Components.Layers;

public partial class AssetTypeManager
{
    private readonly Dictionary<Guid, bool> _visibility = [];
    private string _activeTab = "types";
    private Guid? _expandedTypeId;

    private bool _addingNew;
    private string _newTypeName = string.Empty;
    private string _newTypeColor = "#89b4fa";
    private GeometryType? _newTypeAllowedGeometry;
    private Guid? _newTypeDefaultLayerId;
    private string _newTypeAttributesSchemaJson = string.Empty;
    private string? _newTypeSchemaError;

    protected override void OnInitialized()
    {
        foreach (var t in Repository.GetAssetTypes())
            _visibility[t.Id] = true;
    }

    private void ShowTypesTab() => _activeTab = "types";
    private void ShowStylesTab() => _activeTab = "styles";

    private async Task ToggleLayer(AssetType type, bool visible)
    {
        _visibility[type.Id] = visible;
        await MapInterop.SetLayerVisibilityAsync(MapContext.MapDivId, type.Id.ToString(), visible);
    }

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

    private void SaveNewType()
    {
        if (string.IsNullOrWhiteSpace(_newTypeName)) return;

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
            AttributesSchemaJson = schemaJson.Length > 0 ? schemaJson : null
        };
        Repository.AddAssetType(newType);
        _visibility[newType.Id] = true;
        ResetNewTypeForm();
    }

    private void ResetNewTypeForm()
    {
        _newTypeName = string.Empty;
        _newTypeColor = "#89b4fa";
        _newTypeAllowedGeometry = null;
        _newTypeDefaultLayerId = null;
        _newTypeAttributesSchemaJson = string.Empty;
        _newTypeSchemaError = null;
        _addingNew = false;
    }

    private void DeleteType(AssetType type)
    {
        Repository.DeleteAssetType(type.Id);
        _visibility.Remove(type.Id);
        if (_expandedTypeId == type.Id) _expandedTypeId = null;
    }
}
