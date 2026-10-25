namespace GeoAssets.Shared.Services;

/// <summary>
/// A resolved <see cref="GeoAssets.Core.Models.Layer"/>'s style fields, shaped for the <c>geoassets.js</c> render path
/// (camelCase JSON via the default JS interop serializer, same as <see cref="GeoAssets.Shared.Interfaces.TileLayerOptions"/>).
/// </summary>
public sealed record LayerStyleOptions(
    string Color,
    double Weight,
    double Radius,
    string FillColor,
    double FillOpacity,
    string? DashArray,
    string? IconUrl);
