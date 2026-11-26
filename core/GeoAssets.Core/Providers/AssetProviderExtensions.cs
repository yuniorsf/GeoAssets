using GeoAssets.Core.Interfaces;

namespace GeoAssets.Core.Providers;

/// <summary>
/// Extension helpers over <see cref="IAssetProvider"/> for working with the decorator chain
/// (<c>Observable(Validating(Active(...)))</c> — see XD01-158's decorator-chain-gap note).
/// </summary>
public static class AssetProviderExtensions
{
    /// <summary>
    /// Walks through every <see cref="IProviderDecorator"/> layer wrapping <paramref name="provider"/>
    /// and returns the concrete provider underneath — e.g. the actual <c>PostgresAssetProvider</c>
    /// or <c>ShapefileFeatureStore</c> instance, not one of its decorators. Returns
    /// <paramref name="provider"/> itself when it isn't wrapped at all.
    /// </summary>
    public static IAssetProvider UnwrapToConcrete(this IAssetProvider provider)
    {
        while (provider is IProviderDecorator decorator)
            provider = decorator.Inner;
        return provider;
    }

    /// <summary>
    /// What a provider that doesn't implement <see cref="IProviderCapabilities"/> (nor
    /// <see cref="IWmsProvider"/>) is taken to support: discrete features with every vector action.
    /// </summary>
    public const ProviderRenderCapabilities DefaultVectorCapabilities =
        ProviderRenderCapabilities.VectorFeatures
        | ProviderRenderCapabilities.VectorExport
        | ProviderRenderCapabilities.VectorImport
        | ProviderRenderCapabilities.InteractiveEditing;

    /// <summary>
    /// The concrete provider's <see cref="ProviderRenderCapabilities"/> (XD01-422): looks through
    /// every decorator first (<see cref="UnwrapToConcrete"/>) — a decorator such as
    /// <c>ValidatingAssetProvider</c> or <c>ActiveAssetProvider</c> implements neither interface
    /// itself — then answers the provider's own <see cref="IProviderCapabilities"/>, else
    /// <see cref="ProviderRenderCapabilities.RasterTiles"/> for an <see cref="IWmsProvider"/>,
    /// else <see cref="DefaultVectorCapabilities"/>.
    /// </summary>
    public static ProviderRenderCapabilities GetRenderCapabilities(this IAssetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return provider.UnwrapToConcrete() switch
        {
            IProviderCapabilities declared => declared.RenderCapabilities,
            IWmsProvider                   => ProviderRenderCapabilities.RasterTiles,
            _                              => DefaultVectorCapabilities,
        };
    }
}
