namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Implemented by every <see cref="IAssetProvider"/> decorator in the chain
/// (<c>ObservableAssetProvider</c>, <c>ValidatingAssetProvider</c>, <c>ActiveAssetProvider</c>) to
/// expose the provider it wraps, so <see cref="Providers.AssetProviderExtensions.UnwrapToConcrete"/>
/// can walk through to the concrete provider underneath — see XD01-158's decorator-chain-gap note
/// and XD01-160. Internal: not part of the public <see cref="IAssetProvider"/> contract, only the
/// decorator chain itself needs to expose this.
/// </summary>
internal interface IProviderDecorator
{
    IAssetProvider Inner { get; }
}
