using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Core.Tests.Plugins;

public class ProviderPluginModuleContractTests
{
    [Fact]
    public void ProviderHostTargets_IsAFlagsEnum_WithDistinctBits()
    {
        typeof(ProviderHostTargets).IsDefined(typeof(FlagsAttribute), inherit: false).Should().BeTrue();

        ((int)ProviderHostTargets.None).Should().Be(0);
        ((int)ProviderHostTargets.Server).Should().Be(1);
        ((int)ProviderHostTargets.Browser).Should().Be(2);
        ((int)ProviderHostTargets.MobileDesktop).Should().Be(4);
    }

    [Fact]
    public void ProviderHostTargets_All_IsExactlyEveryHost()
    {
        ProviderHostTargets.All.Should().Be(
            ProviderHostTargets.Server | ProviderHostTargets.Browser | ProviderHostTargets.MobileDesktop);

        Enum.GetValues<ProviderHostTargets>().Should().BeEquivalentTo(
        [
            ProviderHostTargets.None,
            ProviderHostTargets.Server,
            ProviderHostTargets.Browser,
            ProviderHostTargets.MobileDesktop,
            ProviderHostTargets.All,
        ]);
    }

    [Theory]
    [InlineData(ProviderHostTargets.All, ProviderHostTargets.Server, true)]
    [InlineData(ProviderHostTargets.All, ProviderHostTargets.Browser, true)]
    [InlineData(ProviderHostTargets.All, ProviderHostTargets.MobileDesktop, true)]
    [InlineData(ProviderHostTargets.Server, ProviderHostTargets.Browser, false)]
    [InlineData(ProviderHostTargets.Browser | ProviderHostTargets.MobileDesktop, ProviderHostTargets.Server, false)]
    [InlineData(ProviderHostTargets.Browser | ProviderHostTargets.MobileDesktop, ProviderHostTargets.MobileDesktop, true)]
    [InlineData(ProviderHostTargets.None, ProviderHostTargets.Server, false)]
    public void ProviderHostTargets_SupportedHosts_AnswerPerHost(
        ProviderHostTargets supported, ProviderHostTargets host, bool expected)
    {
        supported.HasFlag(host).Should().Be(expected);
    }

    [Fact]
    public void Contracts_LiveInGeoAssetsCore_InTheInterfacesNamespace()
    {
        typeof(IProviderPluginModule).Assembly.Should().BeSameAs(typeof(IProviderPlugin).Assembly);
        typeof(ProviderHostTargets).Assembly.Should().BeSameAs(typeof(IProviderPlugin).Assembly);
        typeof(IProviderPluginModule).Namespace.Should().Be("GeoAssets.Core.Interfaces");
        typeof(ProviderHostTargets).Namespace.Should().Be("GeoAssets.Core.Interfaces");
    }

    [Fact]
    public void Module_RegistersItsPlugin_WithTheHostsConfiguration_BeforeTheContainerIsBuilt()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Providers:Fake:DisplayName"] = "Configured fake" })
            .Build();
        var services = new ServiceCollection();
        IProviderPluginModule module = new FakeModule();

        module.RegisterServices(services, configuration);
        services.AddSingleton<ProviderPluginRegistry>();
        using var provider = services.BuildServiceProvider();

        module.PluginId.Should().Be("fake");
        module.SupportedHosts.Should().Be(ProviderHostTargets.All);
        var plugin = provider.GetRequiredService<ProviderPluginRegistry>().Find("FAKE");
        plugin.Should().NotBeNull();
        plugin!.DisplayName.Should().Be("Configured fake");
    }

    [Fact]
    public void Modules_RegisteringPluginsWithTheSameId_FailWhenTheRegistryResolves()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        new FakeModule().RegisterServices(services, configuration);
        new FakeModule().RegisterServices(services, configuration);
        services.AddSingleton<ProviderPluginRegistry>();
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<ProviderPluginRegistry>();

        act.Should().Throw<InvalidOperationException>().WithMessage("Duplicate provider plugin id 'fake'*");
    }

    private sealed class FakeModule : IProviderPluginModule
    {
        public string PluginId => "fake";
        public ProviderHostTargets SupportedHosts => ProviderHostTargets.All;

        public void RegisterServices(IServiceCollection services, IConfiguration configuration) =>
            services.AddSingleton<IProviderPlugin>(new FakePlugin(configuration["Providers:Fake:DisplayName"] ?? "Fake"));
    }

    private sealed class FakePlugin(string displayName) : IProviderPlugin
    {
        public string Id => "fake";
        public string DisplayName => displayName;
        public string Description => string.Empty;
        public IReadOnlyList<ProviderConfigField> ConfigFields => [];

        public Task<IAssetProvider> CreateAsync(ProviderConfig config, IServiceProvider services, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
