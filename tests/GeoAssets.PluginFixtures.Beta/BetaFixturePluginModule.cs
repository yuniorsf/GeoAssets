using GeoAssets.Core.Interfaces;
using GeoAssets.PluginFixtures.PrivateDependency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.PluginFixtures.Beta;

/// <summary>The fixture plugin's module: registers <see cref="BetaFixtureProviderPlugin"/>.</summary>
public sealed class BetaFixturePluginModule : IProviderPluginModule
{
    /// <summary>The edition of the private dependency this plugin loaded.</summary>
    public static string PrivateDependencyEdition => PrivateDependencyInfo.Edition;

    /// <inheritdoc />
    public string PluginId => "fixture-beta";

    /// <inheritdoc />
    public ProviderHostTargets SupportedHosts => ProviderHostTargets.All;

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton<IProviderPlugin, BetaFixtureProviderPlugin>();
}
