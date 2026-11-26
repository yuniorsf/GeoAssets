using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Provider.Shapefile;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Provider.Shapefile.Tests;

/// <summary>
/// XD01-424 (ADR-002): the Shapefile registrations are split by responsibility — the provider plugin a host needs
/// to open Shapefile catalogs, and the export/import tools a host needs to write or read Shapefiles — so a host
/// that only exports (FieldService.Server) no longer registers a provider plugin as a side effect.
/// </summary>
public sealed class ShapefileServiceExtensionsTests
{
    private static List<(Type Service, Type? Implementation, ServiceLifetime Lifetime)> Registrations(
        Func<IServiceCollection, IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services).Should().BeSameAs(services, "the extension returns the collection for chaining");
        return services.Select(d => (d.ServiceType, d.ImplementationType, d.Lifetime)).ToList();
    }

    [Fact]
    public void AddGeoAssetsShapefilePlugin_RegistersOnlyTheProviderPlugin()
    {
        Registrations(s => s.AddGeoAssetsShapefilePlugin()).Should().Equal(
            (typeof(IProviderPlugin), typeof(ShapefileProviderPlugin), ServiceLifetime.Singleton));
    }

    [Fact]
    public void AddGeoAssetsShapefileTools_RegistersTheExportAndImportServicesAndNoProviderPlugin()
    {
        var registrations = Registrations(s => s.AddGeoAssetsShapefileTools());

        registrations.Should().BeEquivalentTo(new[]
        {
            (typeof(IShapefileExportService), (Type?)typeof(ShapefileExportService), ServiceLifetime.Singleton),
            (typeof(IBdgdPonnotImportService), (Type?)typeof(BdgdPonnotImportService), ServiceLifetime.Singleton),
        });
        registrations.Should().NotContain(r => r.Service == typeof(IProviderPlugin));
    }

    [Fact]
    public void AddGeoAssetsShapefile_RegistersThePluginAndTheTools()
    {
        Registrations(s => s.AddGeoAssetsShapefile()).Should().BeEquivalentTo(
            Registrations(s => s.AddGeoAssetsShapefilePlugin())
                .Concat(Registrations(s => s.AddGeoAssetsShapefileTools())));
    }

    [Fact]
    public void AddGeoAssetsShapefileTools_ResolvesTheToolsAndNoProviderPlugin()
    {
        using var provider = new ServiceCollection().AddGeoAssetsShapefileTools().BuildServiceProvider();

        provider.GetRequiredService<IShapefileExportService>().Should().BeOfType<ShapefileExportService>();
        provider.GetRequiredService<IBdgdPonnotImportService>().Should().BeOfType<BdgdPonnotImportService>();
        provider.GetServices<IProviderPlugin>().Should().BeEmpty();
    }

    [Fact]
    public void AddGeoAssetsShapefilePlugin_ResolvesTheShapefilePlugin()
    {
        using var provider = new ServiceCollection().AddGeoAssetsShapefilePlugin().BuildServiceProvider();

        provider.GetServices<IProviderPlugin>().Should().ContainSingle()
            .Which.Id.Should().Be("shapefile");
        provider.GetService<IShapefileExportService>().Should().BeNull();
    }
}
