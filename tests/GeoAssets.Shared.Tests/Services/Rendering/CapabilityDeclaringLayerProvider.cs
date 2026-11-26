using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Shared.Tests.Services.Rendering;

/// <summary>A provider declaring <paramref name="capabilities"/> through <see cref="IProviderCapabilities"/>.</summary>
internal sealed class CapabilityDeclaringLayerProvider(ProviderRenderCapabilities capabilities, params GeoFeature[] features)
    : FakeLayerProvider(features), IProviderCapabilities
{
    public ProviderRenderCapabilities RenderCapabilities => capabilities;
}
