namespace GeoAssets.Shared.Interfaces;

/// <summary>Options for a Leaflet <c>L.tileLayer</c> or WMS tile layer.</summary>
/// <param name="Attribution">HTML attribution text shown in the map corner.</param>
/// <param name="MaxZoom">Maximum native zoom level for this layer.</param>
/// <param name="MinZoom">Minimum native zoom level for this layer.</param>
/// <param name="Opacity">Layer opacity in [0, 1].</param>
public record TileLayerOptions(
    string Attribution = "",
    int    MaxZoom     = 19,
    int    MinZoom     = 0,
    double Opacity     = 1.0);
