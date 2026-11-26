namespace GeoAssets.Hosting.Abstractions.Plugins;

/// <summary>
/// The provider plugins a host loaded from its plugin directory, each in its own <see cref="PluginLoadContext"/>
/// (ADR-002 D2). Registered as a singleton by
/// <see cref="ProviderPluginDirectoryExtensions.AddProviderPluginsFromDirectory(Microsoft.Extensions.DependencyInjection.IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration, GeoAssets.Core.Plugins.ProviderPluginDiscoveryOptions, GeoAssets.Core.Plugins.IPluginAssemblySource[])"/>;
/// empty until discovery has read the directory.
/// </summary>
public interface IPluginLoadContextRegistry
{
    /// <summary>The loaded plugins, in the order they were loaded.</summary>
    IReadOnlyList<LoadedProviderPlugin> Plugins { get; }

    /// <summary>The loaded plugin named <paramref name="pluginName"/> (case-insensitive), or <see langword="null"/>.</summary>
    LoadedProviderPlugin? Find(string pluginName);
}
