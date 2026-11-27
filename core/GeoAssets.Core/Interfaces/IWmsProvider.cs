namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Marker interface for <see cref="IAssetProvider"/> implementations that are backed by
/// an OGC WMS endpoint and render features as raster image tiles rather than as Leaflet
/// vector layers.
///
/// Read as <see cref="ProviderRenderCapabilities.RasterTiles"/> by
/// <see cref="Providers.AssetProviderExtensions.GetRenderCapabilities"/>; the map's
/// <c>WmsMapLayerRenderer</c> (the one place that reads these members) calls
/// <c>IMapInterop.AddWmsLayerAsync</c> instead of iterating <c>GetAll()</c> (XD01-422).
/// </summary>
public interface IWmsProvider
{
    /// <summary>WMS endpoint base URL, e.g. <c>https://server/api/geoassets/wms</c>.</summary>
    string WmsBaseUrl { get; }

    /// <summary>OGC layer name passed as the <c>LAYERS</c> parameter, e.g. <c>geoassets:feature</c>.</summary>
    string WmsLayerName { get; }

    /// <summary>MIME type for GetMap responses, typically <c>image/png</c>.</summary>
    string WmsFormat { get; }

    /// <summary>
    /// The distributor silo the gateway routes this layer's requests to (its <c>X-Tenant-Domain</c>, e.g.
    /// <c>coelba.local</c>), or <c>null</c> for the gateway's shared kernel or any other WMS server (XD01-473).
    /// </summary>
    string? WmsTenantDomain => null;
}
