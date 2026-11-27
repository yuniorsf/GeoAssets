using System.Reflection;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Plugins;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PMTiles;
using GeoAssets.Provider.PostgreSQL;
using GeoAssets.Provider.Rest;
using GeoAssets.Provider.Shapefile;
using GeoAssets.Provider.WFS;
using GeoAssets.Provider.WMS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Provider.PluginModules.Tests;

/// <summary>
/// XD01-426 (ADR-002 D2): <see cref="ProviderPluginRegistrationExtensions.AddDiscoveredProviderPlugins(IServiceCollection, IConfiguration, ProviderHostTargets, IPluginAssemblySource[])"/>
/// over the five real provider modules (XD01-425), as a host composes them.
/// </summary>
public sealed class ProviderPluginDiscoveryIntegrationTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder().Build();

    private static StaticAssemblyPluginSource FiveModules() => new StaticAssemblyPluginSource()
        .Add<RestProviderPluginModule>()
        .Add<WfsProviderPluginModule>()
        .Add<WmsProviderPluginModule>()
        .Add<ShapefileProviderPluginModule>()
        .Add<PostgresProviderPluginModule>();

    [Theory]
    [InlineData(ProviderHostTargets.Browser)]
    [InlineData(ProviderHostTargets.MobileDesktop)]
    public void AClientHost_GetsEveryProviderButPostgres(ProviderHostTargets host)
    {
        using var provider = BuildHost(host, FiveModules());

        provider.GetRequiredService<ProviderPluginRegistry>().All.Select(p => p.Id)
            .Should().Equal("rest", "shapefile", "wfs", "wms");
    }

    [Fact]
    public void AServerHost_GetsEveryProvider()
    {
        using var provider = BuildHost(ProviderHostTargets.Server, FiveModules());

        provider.GetRequiredService<ProviderPluginRegistry>().All.Select(p => p.Id)
            .Should().Equal("postgres", "rest", "shapefile", "wfs", "wms");
    }

    /// <summary>MAUI's static plugin set (XD01-438, <c>MauiProgram.AddAssetsAndMap</c>): REST and the offline map packs.</summary>
    private static StaticAssemblyPluginSource MauiModules() => new StaticAssemblyPluginSource()
        .Add<RestProviderPluginModule>()
        .Add<PMTilesProviderPluginModule>();

    [Fact]
    public void TheMobileDesktopHost_DiscoversTheOfflinePackPlugin_FromItsStaticSource()
    {
        using var provider = BuildHost(ProviderHostTargets.MobileDesktop, MauiModules());

        var registry = provider.GetRequiredService<ProviderPluginRegistry>();
        registry.All.Select(p => p.Id).Should().Equal("pmtiles", "rest");
        registry.Find("pmtiles").Should().BeOfType<GeoAssets.Provider.PMTiles.Packs.PMTilesPackProviderPlugin>();
    }

    [Fact]
    public void TheBrowserHost_SkipsTheOfflinePackPlugin()
    {
        using var provider = BuildHost(ProviderHostTargets.Browser, MauiModules());

        provider.GetRequiredService<ProviderPluginRegistry>().All.Select(p => p.Id).Should().Equal("rest");
    }

    [Fact]
    public void Discovery_RegistersWhatTheProvidersExtensionsRegister_InPluginIdOrder()
    {
        var viaExtensions = new ServiceCollection();
        viaExtensions.AddGeoAssetsRest();
        viaExtensions.AddGeoAssetsShapefilePlugin();
        viaExtensions.AddGeoAssetsWfs();
        viaExtensions.AddGeoAssetsWms();

        var viaDiscovery = new ServiceCollection();
        viaDiscovery.AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Browser, FiveModules());

        ServiceSignatures.Of(viaDiscovery.Where(d => !IsDiscoveryOwn(d)))
            .Should().Equal(ServiceSignatures.Of(viaExtensions));
    }

    [Fact]
    public void ScanningTheProviderAssemblies_FindsTheSameFiveModules()
    {
        var scanned = new StaticAssemblyPluginSource();
        foreach (var assembly in FiveModules().GetModuleTypes().Select(d => d.ModuleType.Assembly))
            scanned.AddAssembly(assembly);

        scanned.GetModuleTypes().Select(d => d.ModuleType)
            .Should().Equal(FiveModules().GetModuleTypes().Select(d => d.ModuleType));

        var viaScan = new ServiceCollection().AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Server, scanned);
        var viaNames = new ServiceCollection().AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Server, FiveModules());
        ServiceSignatures.Of(viaScan).Should().Equal(ServiceSignatures.Of(viaNames));
    }

    [Fact]
    public void Scan_KeepsPublicModulesOnly_OrderedByFullName()
    {
        var assembly = new TypesAssembly(
        [
            typeof(WmsProviderPluginModule),
            typeof(RestProviderPluginModule),
            typeof(string),
            typeof(RestProviderPlugin),
            typeof(WfsProviderPluginModule),
            typeof(InternalModule),
        ]);

        ProviderPluginModuleScanner.Scan(assembly).Select(d => d.ModuleType).Should().Equal(
            typeof(RestProviderPluginModule), typeof(WfsProviderPluginModule), typeof(WmsProviderPluginModule));
    }

    [Fact]
    public void AHostCallingAProvidersExtensionAndDiscoveringItsModule_FailsOnTheDuplicatePluginId()
    {
        var services = new ServiceCollection();
        services.AddGeoAssetsRest();
        services.AddDiscoveredProviderPlugins(Configuration, ProviderHostTargets.Browser, new StaticAssemblyPluginSource().Add<RestProviderPluginModule>());
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<ProviderPluginRegistry>();

        act.Should().Throw<InvalidOperationException>().WithMessage("Duplicate provider plugin id 'rest'*");
    }

    private static ServiceProvider BuildHost(ProviderHostTargets host, IPluginAssemblySource source)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDiscoveredProviderPlugins(Configuration, host, source);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    /// <summary>What discovery registers itself: the plugin registry and its private ledger.</summary>
    private static bool IsDiscoveryOwn(ServiceDescriptor d) =>
        d.ServiceType == typeof(ProviderPluginRegistry)
        || d.ServiceType.DeclaringType == typeof(ProviderPluginRegistrationExtensions);

    private sealed class InternalModule : IProviderPluginModule
    {
        public string PluginId => "internal";
        public ProviderHostTargets SupportedHosts => ProviderHostTargets.All;

        public void RegisterServices(IServiceCollection services, IConfiguration configuration)
        {
        }
    }

    /// <summary>An assembly answering exactly the given types.</summary>
    private sealed class TypesAssembly(Type[] types) : Assembly
    {
        public override string FullName => "Types.Assembly, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        public override Type[] GetTypes() => types;
    }
}
