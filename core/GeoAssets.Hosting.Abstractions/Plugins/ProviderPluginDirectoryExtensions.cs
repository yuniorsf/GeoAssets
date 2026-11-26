using System.Diagnostics.CodeAnalysis;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Hosting.Abstractions.Plugins;

/// <summary>Registers a server host's provider plugins from its plugin directory (ADR-002 D2).</summary>
public static class ProviderPluginDirectoryExtensions
{
    private const string RequiresUnreferencedCodeMessage =
        "Loads provider plugin assemblies from disk and reads all their types.";

    /// <summary>
    /// Discovers the provider plugins for the host <paramref name="hostTarget"/>, with no allowlist or denylist. See
    /// <see cref="AddProviderPluginsFromDirectory(IServiceCollection, IConfiguration, ProviderPluginDiscoveryOptions, IPluginAssemblySource[])"/>.
    /// </summary>
    [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
    public static IServiceCollection AddProviderPluginsFromDirectory(
        this IServiceCollection services,
        IConfiguration configuration,
        ProviderHostTargets hostTarget,
        params IPluginAssemblySource[] additionalSources) =>
        services.AddProviderPluginsFromDirectory(
            configuration, new ProviderPluginDiscoveryOptions { HostTarget = hostTarget }, additionalSources);

    /// <summary>
    /// Discovers, in one
    /// <see cref="ProviderPluginRegistrationExtensions.AddDiscoveredProviderPlugins(IServiceCollection, IConfiguration, ProviderPluginDiscoveryOptions, IPluginAssemblySource[])"/>
    /// call, the modules <paramref name="additionalSources"/> offer (e.g. a <see cref="StaticAssemblyPluginSource"/> of
    /// the host's bundled providers) and those of the plugin directory (<see cref="DirectoryPluginAssemblySource"/>),
    /// and registers the directory's loaded plugins as the <see cref="IPluginLoadContextRegistry"/> singleton.
    /// The directory is <see cref="DirectoryPluginAssemblySource.DirectoryConfigurationKey"/>, relative to the host's
    /// base directory; it must exist when configured. When it isn't configured,
    /// <see cref="DirectoryPluginAssemblySource.DefaultDirectory"/> is read if it exists — a host without dynamic
    /// plugins needs no directory. <see cref="DirectoryPluginAssemblySource.FlatPluginFilePatternConfigurationKey"/>, when
    /// set, turns on the flat layout.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The configured directory doesn't exist.</exception>
    /// <exception cref="ArgumentException">As <see cref="ProviderPluginRegistrationExtensions.AddDiscoveredProviderPlugins(IServiceCollection, IConfiguration, ProviderPluginDiscoveryOptions, IPluginAssemblySource[])"/>.</exception>
    /// <exception cref="InvalidOperationException">A plugin is broken, or as <see cref="ProviderPluginRegistrationExtensions.AddDiscoveredProviderPlugins(IServiceCollection, IConfiguration, ProviderPluginDiscoveryOptions, IPluginAssemblySource[])"/>.</exception>
    [RequiresUnreferencedCode(RequiresUnreferencedCodeMessage)]
    public static IServiceCollection AddProviderPluginsFromDirectory(
        this IServiceCollection services,
        IConfiguration configuration,
        ProviderPluginDiscoveryOptions options,
        params IPluginAssemblySource[] additionalSources)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(additionalSources);

        var configured = configuration[DirectoryPluginAssemblySource.DirectoryConfigurationKey];
        var directory = new DirectoryPluginAssemblySource(
            string.IsNullOrWhiteSpace(configured) ? DirectoryPluginAssemblySource.DefaultDirectory : configured,
            configuration[DirectoryPluginAssemblySource.FlatPluginFilePatternConfigurationKey]);
        var readDirectory = !string.IsNullOrWhiteSpace(configured) || Directory.Exists(directory.RootDirectory);

        IPluginAssemblySource[] sources = readDirectory ? [.. additionalSources, directory] : additionalSources;
        if (sources.Length == 0)
            sources = [new StaticAssemblyPluginSource()];

        services.AddDiscoveredProviderPlugins(configuration, options, sources);
        services.AddSingleton<IPluginLoadContextRegistry>(directory);
        return services;
    }
}
