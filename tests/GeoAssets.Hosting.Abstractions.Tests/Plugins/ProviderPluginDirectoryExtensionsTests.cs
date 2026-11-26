using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Plugins;
using GeoAssets.Core.Services;
using GeoAssets.Hosting.Abstractions.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Hosting.Abstractions.Tests.Plugins;

/// <summary>XD01-427: <see cref="ProviderPluginDirectoryExtensions.AddProviderPluginsFromDirectory(IServiceCollection, IConfiguration, ProviderPluginDiscoveryOptions, IPluginAssemblySource[])"/>.</summary>
public sealed class ProviderPluginDirectoryExtensionsTests : IDisposable
{
    private readonly TempPluginDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void TheConfiguredDirectory_AndTheOtherSources_RegisterInOneDiscovery()
    {
        var services = new ServiceCollection();

        services.AddProviderPluginsFromDirectory(
            Configured(PluginFixtures.Root),
            ProviderHostTargets.Server,
            new StaticAssemblyPluginSource().Add<DirectoryPluginAssemblySourceTests.BundledModule>());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ProviderPluginRegistry>().All.Select(p => p.Id)
            .Should().BeEquivalentTo("bundled", "fixture-alpha", "fixture-beta");
        var registry = provider.GetRequiredService<IPluginLoadContextRegistry>();
        registry.Plugins.Select(p => p.Name).Should().Equal(PluginFixtures.Alpha, PluginFixtures.Beta);
        registry.Should().BeOfType<DirectoryPluginAssemblySource>().Which.RootDirectory.Should().Be(PluginFixtures.Root);
    }

    [Fact]
    public void TheDiscoveryOptions_Apply_ToTheDirectorysModules()
    {
        var services = new ServiceCollection();
        var options = new ProviderPluginDiscoveryOptions { HostTarget = ProviderHostTargets.Server };
        options.DisabledPluginIds.Add("fixture-alpha");

        services.AddProviderPluginsFromDirectory(Configured(PluginFixtures.Root), options);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ProviderPluginRegistry>().All.Select(p => p.Id).Should().Equal("fixture-beta");
    }

    [Fact]
    public void AConfiguredFlatPattern_TurnsOnTheFlatLayout()
    {
        _temp.CopyPluginFlat(PluginFixtures.Beta, "GeoAssets.Provider.FixtureBeta.dll");
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [DirectoryPluginAssemblySource.DirectoryConfigurationKey] = _temp.Root,
                [DirectoryPluginAssemblySource.FlatPluginFilePatternConfigurationKey] = DirectoryPluginAssemblySourceTests.FlatPattern,
            })
            .Build();

        services.AddProviderPluginsFromDirectory(configuration, ProviderHostTargets.Server);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ProviderPluginRegistry>().All.Select(p => p.Id).Should().Equal("fixture-beta");
        provider.GetRequiredService<IPluginLoadContextRegistry>().Plugins.Select(p => p.Name)
            .Should().Equal("GeoAssets.Provider.FixtureBeta");
    }

    [Fact]
    public void AConfiguredDirectoryThatDoesNotExist_FailsFast()
    {
        var missing = Path.Combine(_temp.Root, "missing");

        var act = () => new ServiceCollection().AddProviderPluginsFromDirectory(Configured(missing), ProviderHostTargets.Server);

        act.Should().Throw<DirectoryNotFoundException>().WithMessage($"*'{missing}'*");
    }

    [Fact]
    public void WithNoDirectoryConfigured_AMissingDefaultDirectory_LoadsNoPlugins()
    {
        var services = new ServiceCollection();
        var defaultRoot = Path.GetFullPath(DirectoryPluginAssemblySource.DefaultDirectory, AppContext.BaseDirectory);
        Directory.Exists(defaultRoot).Should().BeFalse("the test host ships no plugins/providers directory");

        services.AddProviderPluginsFromDirectory(
            new ConfigurationBuilder().Build(),
            ProviderHostTargets.Server,
            new StaticAssemblyPluginSource().Add<DirectoryPluginAssemblySourceTests.BundledModule>());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ProviderPluginRegistry>().All.Select(p => p.Id).Should().Equal("bundled");
        var registry = provider.GetRequiredService<IPluginLoadContextRegistry>();
        registry.Plugins.Should().BeEmpty();
        registry.Should().BeOfType<DirectoryPluginAssemblySource>().Which.RootDirectory.Should().Be(defaultRoot);
    }

    [Fact]
    public void WithNoSourceAtAll_TheRegistryIsStillRegistered()
    {
        var services = new ServiceCollection();

        services.AddProviderPluginsFromDirectory(new ConfigurationBuilder().Build(), ProviderHostTargets.Server);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ProviderPluginRegistry>().All.Should().BeEmpty();
        provider.GetRequiredService<IPluginLoadContextRegistry>().Plugins.Should().BeEmpty();
    }

    [Fact]
    public void NullArguments_AreRejected()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var options = new ProviderPluginDiscoveryOptions { HostTarget = ProviderHostTargets.Server };

        FluentActions.Invoking(() => ((IServiceCollection)null!).AddProviderPluginsFromDirectory(configuration, options))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => services.AddProviderPluginsFromDirectory(null!, options))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => services.AddProviderPluginsFromDirectory(configuration, (ProviderPluginDiscoveryOptions)null!))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => services.AddProviderPluginsFromDirectory(configuration, options, null!))
            .Should().Throw<ArgumentNullException>();
    }

    private static IConfiguration Configured(string directory) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [DirectoryPluginAssemblySource.DirectoryConfigurationKey] = directory,
        })
        .Build();
}
