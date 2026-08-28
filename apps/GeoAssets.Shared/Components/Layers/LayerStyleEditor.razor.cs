using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Shared.Components.Layers;

public partial class LayerStyleEditor
{
    private bool _addingNew;
    private string _name = string.Empty;
    private GeometryType _geometryType = GeometryType.Point;
    private string _color = "#3388ff";
    private double _radius = 8;
    private string _iconUrl = string.Empty;
    private double _weight = 3;
    private string _dashArray = string.Empty;
    private string _fillColor = "#3388ff";
    private double _fillOpacity = 0.2;

    private string GeometryTypeLabel(GeometryType type) => type switch
    {
        GeometryType.Point => L["map.draw.point"],
        GeometryType.LineString => L["map.draw.line"],
        GeometryType.Polygon => L["map.draw.polygon"],
        _ => type.ToString()
    };

    private void OnGeometryTypeChanged(string? value)
    {
        if (value is not null)
            _geometryType = Enum.Parse<GeometryType>(value);
    }

    private void SaveNewLayer()
    {
        if (string.IsNullOrWhiteSpace(_name)) return;

        var layer = new Layer
        {
            Name = _name.Trim(),
            GeometryType = _geometryType,
            Color = _color,
            Radius = _radius,
            IconUrl = _iconUrl.Trim(),
            Weight = _weight,
            DashArray = string.IsNullOrWhiteSpace(_dashArray) ? null : _dashArray.Trim(),
            FillColor = _fillColor,
            FillOpacity = _fillOpacity
        };
        Repository.AddLayer(layer);
        ResetForm();
    }

    private void ResetForm()
    {
        _name = string.Empty;
        _geometryType = GeometryType.Point;
        _color = "#3388ff";
        _radius = 8;
        _iconUrl = string.Empty;
        _weight = 3;
        _dashArray = string.Empty;
        _fillColor = "#3388ff";
        _fillOpacity = 0.2;
        _addingNew = false;
    }

    private void DeleteLayer(Layer layer) => Repository.DeleteLayer(layer.Id);
}
