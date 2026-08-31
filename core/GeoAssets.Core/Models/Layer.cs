using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Core.Models;

/// <summary>
/// A reusable map style, mirroring Leaflet's own path/marker style options. A single instance
/// targets one <see cref="GeometryType"/> — only the fields relevant to that shape are meaningful
/// (e.g. <see cref="Radius"/> only applies to <see cref="Geometry.GeometryType.Point"/>). Resolved
/// onto features by the style-resolution service built in XD01-111.
/// </summary>
public sealed class Layer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>Which geometry shape this style applies to.</summary>
    public GeometryType GeometryType { get; set; }

    /// <summary>
    /// Marker color (<see cref="Geometry.GeometryType.Point"/>) or stroke color
    /// (<see cref="Geometry.GeometryType.LineString"/>/<see cref="Geometry.GeometryType.Polygon"/>).
    /// Leaflet's <c>color</c> path/marker option.
    /// </summary>
    public string Color { get; set; } = "#3388ff";

    /// <summary>Marker radius in pixels. Only meaningful for <see cref="Geometry.GeometryType.Point"/>. Leaflet's <c>radius</c> option.</summary>
    public double Radius { get; set; } = 8;

    /// <summary>Marker icon URL. Only meaningful for <see cref="Geometry.GeometryType.Point"/>.</summary>
    public string IconUrl { get; set; } = string.Empty;

    /// <summary>
    /// Stroke weight in pixels. Meaningful for <see cref="Geometry.GeometryType.LineString"/> and
    /// <see cref="Geometry.GeometryType.Polygon"/>. Leaflet's <c>weight</c> option.
    /// </summary>
    public double Weight { get; set; } = 3;

    /// <summary>Dash pattern (e.g. <c>"5, 5"</c>). Only meaningful for <see cref="Geometry.GeometryType.LineString"/>. Leaflet's <c>dashArray</c> option.</summary>
    public string? DashArray { get; set; }

    /// <summary>Fill color. Only meaningful for <see cref="Geometry.GeometryType.Polygon"/>. Leaflet's <c>fillColor</c> option.</summary>
    public string FillColor { get; set; } = "#3388ff";

    /// <summary>Fill opacity (0–1). Only meaningful for <see cref="Geometry.GeometryType.Polygon"/>. Leaflet's <c>fillOpacity</c> option.</summary>
    public double FillOpacity { get; set; } = 0.2;
}
