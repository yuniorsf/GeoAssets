using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.PluginFixtures.PrivateDependency;

namespace GeoAssets.PluginFixtures.Alpha;

/// <summary>A provider plugin that creates no provider: it only shows its registration crossed the load context.</summary>
internal sealed class AlphaFixtureProviderPlugin : IProviderPlugin
{
    public string Id => "fixture-alpha";

    public string DisplayName => $"Fixture Alpha ({PrivateDependencyInfo.Edition})";

    public string Description => "XD01-427 test fixture.";

    public IReadOnlyList<ProviderConfigField> ConfigFields => [];

    public Task<IAssetProvider> CreateAsync(ProviderConfig config, IServiceProvider services, CancellationToken ct = default) =>
        throw new NotSupportedException("The fixture plugin creates no provider.");
}
