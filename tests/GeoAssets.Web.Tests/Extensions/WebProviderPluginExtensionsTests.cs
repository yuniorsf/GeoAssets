using System.Globalization;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Plugins;
using GeoAssets.Core.Services;
using GeoAssets.Provider.Rest;
using GeoAssets.Provider.Shapefile;
using GeoAssets.Provider.WFS;
using GeoAssets.Provider.WMS;
using GeoAssets.Web.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Web.Tests.Extensions;

/// <summary>
/// XD01-428 (ADR-002 D2): the Web host's provider plugins, as <c>Program</c> composes them — discovered from its bundled
/// modules for the browser host, offering exactly the providers and tools it registered by hand before.
/// </summary>
public sealed class WebProviderPluginExtensionsTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder().Build();

    [Fact]
    public void AddWebProviderPlugins_OffersRestShapefileWfsAndWms()
    {
        using var provider = BuildHost();

        provider.GetRequiredService<ProviderPluginRegistry>().All.Select(p => p.Id)
            .Should().Equal("rest", "shapefile", "wfs", "wms");
    }

    [Fact]
    public void AddWebProviderPlugins_KeepsTheShapefileTools_ThePluginModuleDoesNotRegister()
    {
        using var provider = BuildHost();

        provider.GetService<IShapefileExportService>().Should().BeOfType<ShapefileExportService>();
        provider.GetService<IBdgdPonnotImportService>().Should().NotBeNull();
    }

    [Fact]
    public void AddWebProviderPlugins_RegistersWhatTheHandWrittenRegistrationDid()
    {
        var before = new ServiceCollection();
        before.AddGeoAssetsRest();
        before.AddGeoAssetsWfs();
        before.AddGeoAssetsWms();
        before.AddGeoAssetsShapefile();
        before.AddSingleton<ProviderPluginRegistry>();

        var after = new ServiceCollection().AddWebProviderPlugins(Configuration);

        Signatures(after.Where(d => d.ServiceType.DeclaringType != typeof(ProviderPluginRegistrationExtensions)))
            .Should().BeEquivalentTo(Signatures(before));
    }

    [Fact]
    public void AddWebProviderPlugins_RegistersEachPluginOnce()
    {
        var services = new ServiceCollection().AddWebProviderPlugins(Configuration);

        services.Where(d => d.ServiceType == typeof(IProviderPlugin)).Should().HaveCount(4);
        services.Where(d => d.ServiceType == typeof(ProviderPluginRegistry)).Should().ContainSingle();
    }

    private static ServiceProvider BuildHost()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddWebProviderPlugins(Configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private static List<string> Signatures(IEnumerable<ServiceDescriptor> descriptors) =>
    [
        .. descriptors.Select(d =>
            $"{d.ServiceType.FullName} => {Implementation(d)} ({d.Lifetime.ToString()}{(d.IsKeyedService ? ", key " + Convert.ToString(d.ServiceKey, CultureInfo.InvariantCulture) : "")})"),
    ];

    private static string Implementation(ServiceDescriptor d) => d.IsKeyedService
        ? d.KeyedImplementationType?.FullName ?? d.KeyedImplementationInstance?.GetType().FullName ?? "factory"
        : d.ImplementationType?.FullName ?? d.ImplementationInstance?.GetType().FullName ?? "factory";
}
