using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.PluginFixtures.PrivateDependency;

namespace GeoAssets.PluginFixtures.Beta;

/// <summary>A provider plugin that creates no provider: it only shows its registration crossed the load context.</summary>
internal sealed class BetaFixtureProviderPlugin : IProviderPlugin
{
    public string Id => "fixture-beta";

    public string DisplayName => $"Fixture Beta ({PrivateDependencyInfo.Edition})";

    public string Description => "XD01-427 test fixture.";

    public IReadOnlyList<ProviderConfigField> ConfigFields => [];

    public Task<IAssetProvider> CreateAsync(ProviderConfig config, IServiceProvider services, CancellationToken ct = default) =>
        throw new NotSupportedException("The fixture plugin creates no provider.");
}
