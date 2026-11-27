using System.Collections.Concurrent;
using System.Reflection;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.VectorFiles;
using GeoAssets.Hosting.Abstractions.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.Gdal.PluginLoading.Tests;

/// <summary>
/// A server-shaped host for the tests: discovers <c>plugins/providers/</c> (where this project's build packaged the GDAL
/// plugin) with <see cref="ProviderPluginDirectoryExtensions.AddProviderPluginsFromDirectory(IServiceCollection, IConfiguration, ProviderHostTargets, GeoAssets.Core.Plugins.IPluginAssemblySource[])"/>,
/// builds the container, then makes GDAL's first use in this process from <see cref="Threads"/> threads at once —
/// each asking the plugin's <see cref="IVectorFileReader"/> (a <c>GeoAssets.Core</c> contract) whether FlatGeobuf is
/// available, which configures native GDAL inside the plugin's load context. One per test process
/// (<see cref="Instance"/>): native GDAL is process-wide, and so is the first use these tests observe.
/// </summary>
internal sealed class GdalPluginHost
{
    public const string PluginName = "GeoAssets.Provider.Gdal";
    public const int Threads = 16;

    private static readonly Lazy<GdalPluginHost> Shared = new(() => new GdalPluginHost(), LazyThreadSafetyMode.ExecutionAndPublication);

    private GdalPluginHost()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddProviderPluginsFromDirectory(configuration, ProviderHostTargets.Server);
        Services = services.BuildServiceProvider();

        Plugin = Services.GetRequiredService<IPluginLoadContextRegistry>().Find(PluginName)
            ?? throw new InvalidOperationException($"The {PluginName} plugin was not loaded from plugins/providers/.");

        var reader = Services.GetRequiredService<IVectorFileReader>();
        var results = new ConcurrentBag<bool>();
        using var start = new Barrier(Threads);
        var threads = Enumerable.Range(0, Threads).Select(_ => new Thread(() =>
        {
            start.SignalAndWait();
            results.Add(reader.IsAvailable(VectorFileFormat.FlatGeobuf));
        })).ToArray();
        foreach (var thread in threads)
            thread.Start();
        foreach (var thread in threads)
            thread.Join();

        FirstUseResults = [.. results];
    }

    /// <summary>The process's host, built on first use.</summary>
    public static GdalPluginHost Instance => Shared.Value;

    public ServiceProvider Services { get; }

    public LoadedProviderPlugin Plugin { get; }

    /// <summary>What each of the <see cref="Threads"/> concurrent first uses answered.</summary>
    public IReadOnlyList<bool> FirstUseResults { get; }

    /// <summary>A public static member of the plugin's <c>GdalNativeInitializer</c>, read in the plugin's context.</summary>
    public static object? Initializer(Assembly pluginAssembly, string member)
    {
        ArgumentNullException.ThrowIfNull(pluginAssembly);
        var type = pluginAssembly.GetType("GeoAssets.Provider.Gdal.GdalNativeInitializer", throwOnError: true)!;
        var property = type.GetProperty(member);
        return property is not null ? property.GetValue(null) : type.GetMethod(member)!.Invoke(null, null);
    }
}
