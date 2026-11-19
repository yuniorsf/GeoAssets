using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Server.Tests.Catalogs;

/// <summary>An <see cref="IProviderPlugin"/> whose catalog can never be built.</summary>
internal sealed class ThrowingProviderPlugin(string id) : IProviderPlugin
{
    public string Id { get; } = id;
    public string DisplayName => "Throwing";
    public string Description => "Test double";
    public IReadOnlyList<ProviderConfigField> ConfigFields => [];

    public Task<IAssetProvider> CreateAsync(ProviderConfig config, IServiceProvider services, CancellationToken ct = default) =>
        throw new InvalidOperationException("the backing store is unreachable");
}
