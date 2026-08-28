using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Provider.PostgreSQL.Entities;

/// <summary>EF Core entity that maps to the <c>layer</c> table.</summary>
public sealed class LayerRow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public GeometryType GeometryType { get; set; }
    public string Color { get; set; } = "#3388ff";
    public double Radius { get; set; } = 8;
    public string IconUrl { get; set; } = string.Empty;
    public double Weight { get; set; } = 3;
    public string? DashArray { get; set; }
    public string FillColor { get; set; } = "#3388ff";
    public double FillOpacity { get; set; } = 0.2;
}
