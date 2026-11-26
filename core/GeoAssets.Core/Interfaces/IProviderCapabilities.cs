namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Implemented by an <see cref="IAssetProvider"/> that declares its own
/// <see cref="ProviderRenderCapabilities"/> (ADR-002 D2, XD01-422). A provider that doesn't is
/// read as <see cref="Providers.AssetProviderExtensions.DefaultVectorCapabilities"/> — see
/// <see cref="Providers.AssetProviderExtensions.GetRenderCapabilities"/>, which looks through the
/// decorator chain first.
/// </summary>
public interface IProviderCapabilities
{
    /// <summary>What the provider renders and which vector actions it supports.</summary>
    ProviderRenderCapabilities RenderCapabilities { get; }
}
