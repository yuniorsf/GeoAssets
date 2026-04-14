namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Marker interface for <see cref="IAssetProvider"/> implementations that are backed by
/// an OGC WMS endpoint and render features as raster image tiles rather than as Leaflet
/// vector layers.
///
/// When the pool panel detects this interface on a connected provider it calls
/// <c>IMapInterop.AddWmsLayerAsync</c> instead of iterating <c>GetAll()</c>.
/// </summary>
public interface IWmsProvider
{
    /// <summary>WMS endpoint base URL, e.g. <c>https://server/api/geoassets/wms</c>.</summary>
    string WmsBaseUrl { get; }

    /// <summary>OGC layer name passed as the <c>LAYERS</c> parameter, e.g. <c>geoassets:feature</c>.</summary>
    string WmsLayerName { get; }

    /// <summary>MIME type for GetMap responses, typically <c>image/png</c>.</summary>
    string WmsFormat { get; }
}
