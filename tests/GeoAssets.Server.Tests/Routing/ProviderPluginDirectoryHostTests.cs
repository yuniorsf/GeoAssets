using System.Runtime.Loader;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Services;
using GeoAssets.Hosting.Abstractions.Plugins;
using GeoAssets.Provider.PostgreSQL;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Routing;

/// <summary>
/// XD01-428 (ADR-002 D2): the real <c>Program</c> composes its provider plugins through discovery — the postgres-spatial
/// module's <c>postgres</c> plugin, and whatever drop-in plugins its plugin directory holds, each in its own load context
/// and implementing the host's own <see cref="IProviderPlugin"/>.
/// </summary>
[Trait("Category", "RouteInventory")]
public sealed class ProviderPluginDirectoryHostTests
{
    /// <summary>Where the Server.Tests project packages the fixture plugin (its <c>ProviderPluginsDirectory</c>).</summary>
    private const string FixtureDirectory = "provider-plugin-fixtures";

    private const string FixturePluginName = "GeoAssets.PluginFixtures.Beta";

    [Fact]
    public async Task ServerHost_OffersThePostgresPluginOnly()
    {
        var services = await ServerHostFixture.GetServicesAsync();

        services.GetRequiredService<ProviderPluginRegistry>().All.Select(p => p.Id).Should().Equal("postgres");
        services.GetRequiredService<ProviderPluginRegistry>().Find("postgres").Should().BeOfType<PostgresProviderPlugin>();
        services.GetRequiredService<IPluginLoadContextRegistry>().Plugins.Should().BeEmpty(
            "the test output has no plugins/providers directory, and an unconfigured, absent default directory is skipped");
    }

    [Fact]
    public async Task ServerHost_WithAPluginDirectory_LoadsItsPluginsNextToPostgres()
    {
        var factory = await ServerHostFixture.GetProviderPluginDirectoryFactoryAsync(FixtureDirectory);
        var registry = factory.Services.GetRequiredService<ProviderPluginRegistry>();

        registry.All.Select(p => p.Id).Should().BeEquivalentTo(["postgres", "fixture-beta"]);
        registry.Find("postgres").Should().BeOfType<PostgresProviderPlugin>();
    }

    [Fact]
    public async Task ServerHost_DirectoryLoadedPlugin_ImplementsTheHostsProviderPluginContract()
    {
        var factory = await ServerHostFixture.GetProviderPluginDirectoryFactoryAsync(FixtureDirectory);
        var plugin = factory.Services.GetRequiredService<ProviderPluginRegistry>().Find("fixture-beta");
        var loaded = factory.Services.GetRequiredService<IPluginLoadContextRegistry>().Find(FixturePluginName);

        plugin.Should().NotBeNull();
        loaded.Should().NotBeNull();
        AssemblyLoadContext.GetLoadContext(plugin!.GetType().Assembly).Should().BeSameAs(loaded!.LoadContext);
        plugin.GetType().GetInterfaces().Should().Contain(typeof(IProviderPlugin));
        AssemblyLoadContext.GetLoadContext(typeof(IProviderPlugin).Assembly).Should().BeSameAs(AssemblyLoadContext.Default,
            "the shared contracts come from the host's own context (ADR-002 D3)");
        loaded.LoadContext.Assemblies.Should().NotContain(a => string.Equals(a.GetName().Name, "GeoAssets.Core", StringComparison.Ordinal));
    }
}
