using GeoAssets.Core.Interfaces;
using GeoAssets.PluginFixtures.PrivateDependency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.PluginFixtures.Alpha;

/// <summary>The fixture plugin's module: registers <see cref="AlphaFixtureProviderPlugin"/>.</summary>
public sealed class AlphaFixturePluginModule : IProviderPluginModule
{
    /// <summary>The edition of the private dependency this plugin loaded.</summary>
    public static string PrivateDependencyEdition => PrivateDependencyInfo.Edition;

    /// <inheritdoc />
    public string PluginId => "fixture-alpha";

    /// <inheritdoc />
    public ProviderHostTargets SupportedHosts => ProviderHostTargets.Server;

    /// <summary>The SQLite version number of the native <c>e_sqlite3</c> library this plugin carries.</summary>
    public static int NativeLibraryVersion() => NativeMethods.LibVersionNumber();

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton<IProviderPlugin, AlphaFixtureProviderPlugin>();
}
