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
}
