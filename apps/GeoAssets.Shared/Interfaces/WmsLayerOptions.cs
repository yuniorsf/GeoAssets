namespace GeoAssets.Shared.Interfaces;

/// <summary>Options for a Leaflet <c>L.tileLayer.wms()</c> OGC WMS layer.</summary>
/// <param name="Layers">Comma-separated OGC LAYERS parameter, e.g. <c>geoassets:feature</c>.</param>
/// <param name="Format">Image MIME type returned by GetMap, e.g. <c>image/png</c>.</param>
/// <param name="Transparent">Whether the WMS server should return transparent PNG tiles.</param>
/// <param name="Attribution">HTML attribution text shown in the map corner.</param>
/// <param name="Version">WMS protocol version (default 1.1.1).</param>
/// <param name="MaxZoom">Maximum native zoom level.</param>
/// <param name="Opacity">Layer opacity in [0, 1].</param>
public record WmsLayerOptions(
    string Layers      = "geoassets:feature",
    string Format      = "image/png",
    bool   Transparent = true,
    string Attribution = "",
    string Version     = "1.1.1",
    int    MaxZoom     = 19,
    double Opacity     = 1.0);
