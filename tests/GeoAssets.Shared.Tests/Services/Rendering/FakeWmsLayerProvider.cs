using GeoAssets.Core.Interfaces;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>An <see cref="IWmsProvider"/> that declares no <see cref="IProviderCapabilities"/> — read
/// as <see cref="ProviderRenderCapabilities.RasterTiles"/> by the fallback.</summary>
internal sealed class FakeWmsLayerProvider() : FakeLayerProvider(), IWmsProvider
{
    public string WmsBaseUrl => "https://wms.example.com/geoassets/wms";
    public string WmsLayerName => "geoassets:layer";
    public string WmsFormat => "image/png";
}
