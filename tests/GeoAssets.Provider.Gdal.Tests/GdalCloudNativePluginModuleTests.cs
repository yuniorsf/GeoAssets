using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Plugins;
using GeoAssets.Core.Rendering;
using GeoAssets.Core.VectorFiles;
using GeoAssets.Provider.Gdal.Cog;
using GeoAssets.Provider.Gdal.FileGeodatabase;
using GeoAssets.Provider.PMTiles;
using GeoAssets.Provider.Gdal.Tests.Cog;
using GeoAssets.Provider.PMTiles.Tests;
using GeoAssets.Provider.Gdal.VectorFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Provider.Gdal.Tests;

public sealed class GdalCloudNativePluginModuleTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private string PMTilesFile() =>
        new PMTilesTestArchive().Add(new TileAddress(0, 0, 0), PMTilesTestArchive.Gzip(TestVectorTile.Encode("roads", "water"))).WriteTo(_directory.Path);

    [Fact]
    public void TheModule_IsGdalCloudNative_ForServersAndMauiMobileDesktop()
    {
        var module = new GdalCloudNativePluginModule();

        module.PluginId.Should().Be("gdal-cloud-native");
        module.SupportedHosts.Should().Be(ProviderHostTargets.Server | ProviderHostTargets.MobileDesktop);
    }

    [Fact]
    public void WithNothingConfigured_ItRegistersTheFormats_AndNoTileSource()
    {
        var services = new ServiceCollection();

        new GdalCloudNativePluginModule(nativeRuntimeAvailable: true).RegisterServices(services, Configuration());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IFileGeodatabaseExportService>().Should().BeOfType<FileGeodatabaseExportService>();
        provider.GetRequiredService<IVectorFileReader>().Should().BeOfType<OgrVectorFileReader>();
        provider.GetRequiredService<IVectorFileWriter>().Should().BeOfType<OgrVectorFileWriter>();
        provider.GetService<IVectorTileSource>().Should().BeNull("the module never replaces a host's unkeyed tile source");
        provider.GetService<IRasterTileSource>().Should().BeNull();
        services.Should().NotContain(d => d.ServiceType == typeof(IProviderPlugin));
    }

    [Fact]
    public async Task ConfiguredArchivesAndRasters_AreKeyedTileSources()
    {
        var pmtiles = PMTilesFile();
        var cog = TestRasters.WriteCog(_directory.Path, (1, 2, 3));
        var services = new ServiceCollection();

        new GdalCloudNativePluginModule(nativeRuntimeAvailable: true).RegisterServices(services, Configuration(
            ("GeoAssets:Gdal:PMTiles:basemap:Path", pmtiles),
            ("GeoAssets:Gdal:Cog:ortho:Path", cog)));

        await using var provider = services.BuildServiceProvider();
        var vector = provider.GetRequiredKeyedService<IVectorTileSource>("basemap").Should().BeOfType<PMTilesVectorTileSource>().Subject;
        vector.Path.Should().Be(pmtiles);
        var raster = provider.GetRequiredKeyedService<IRasterTileSource>("ortho").Should().BeOfType<CogRasterTileSource>().Subject;
        raster.LayerId.Should().Be("ortho");
        raster.Path.Should().Be(cog);
        (await vector.GetVectorTileAsync(new TileRequest(new TileLayerSet("basemap", ["roads", "water"]), new TileAddress(0, 0, 0)), CancellationToken.None))
            .IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void WithoutTheNativeRuntime_OnlyPMTilesIsRegistered()
    {
        var services = new ServiceCollection();

        new GdalCloudNativePluginModule(nativeRuntimeAvailable: false).RegisterServices(services, Configuration(
            ("GeoAssets:Gdal:PMTiles:basemap:Path", PMTilesFile())));

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredKeyedService<IVectorTileSource>("basemap").Should().BeOfType<PMTilesVectorTileSource>();
        provider.GetService<IFileGeodatabaseExportService>().Should().BeNull();
        provider.GetService<IVectorFileReader>().Should().BeNull();
        provider.GetService<IVectorFileWriter>().Should().BeNull();
    }

    [Fact]
    public void WithoutTheNativeRuntime_AConfiguredCog_FailsTheStartup()
    {
        var act = () => new GdalCloudNativePluginModule(nativeRuntimeAvailable: false).RegisterServices(new ServiceCollection(), Configuration(
            ("GeoAssets:Gdal:Cog:ortho:Path", "/vsicurl/https://example.invalid/ortho.tif")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*no native GDAL runtime*");
    }

    [Theory]
    [InlineData("GeoAssets:Gdal:PMTiles:bad id:Path", "x.pmtiles", "*not a tile key segment*")]
    [InlineData("GeoAssets:Gdal:PMTiles:basemap:Other", "x", "*Path' is required*")]
    [InlineData("GeoAssets:Gdal:PMTiles:basemap:Path", "missing.pmtiles", "*does not exist*")]
    [InlineData("GeoAssets:Gdal:Cog:ortho:Path", "missing.tif", "*does not exist*")]
    public void InvalidConfiguration_FailsFast(string key, string value, string message)
    {
        var act = () => new GdalCloudNativePluginModule(nativeRuntimeAvailable: true).RegisterServices(new ServiceCollection(), Configuration((key, value)));

        act.Should().Throw<InvalidOperationException>().WithMessage(message);
    }

    [Fact]
    public void ARemoteCog_IsNotCheckedOnDisk()
    {
        var services = new ServiceCollection();

        new GdalCloudNativePluginModule(nativeRuntimeAvailable: true).RegisterServices(services, Configuration(
            ("GeoAssets:Gdal:Cog:ortho:Path", "/vsicurl/https://example.invalid/ortho.tif")));

        services.Should().Contain(d => d.ServiceType == typeof(IRasterTileSource) && Equals(d.ServiceKey, "ortho"));
    }

    [Theory]
    [InlineData(ProviderHostTargets.Server, true)]
    [InlineData(ProviderHostTargets.MobileDesktop, true)]
    [InlineData(ProviderHostTargets.Browser, false)]
    public void Discovery_RunsTheModule_OnlyOnItsHosts(ProviderHostTargets host, bool registered)
    {
        var services = new ServiceCollection();

        services.AddDiscoveredProviderPlugins(Configuration(), host, new StaticAssemblyPluginSource().Add<GdalCloudNativePluginModule>());

        services.Any(d => d.ServiceType == typeof(IFileGeodatabaseExportService)).Should().Be(registered);
    }
}
