using Bunit;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Localization;
using GeoAssets.Core.Models;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Services;
using GeoAssets.Provider.WMS;
using GeoAssets.Shared.Components.Providers;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Shared.Services.Rendering;
using GeoAssets.Shared.Tests.Components.ServiceOrders;
using GeoAssets.Shared.Tests.Services.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Providers;

/// <summary>
/// XD01-422 — <see cref="ProviderPoolPanel"/> draws and hides entries through the
/// <see cref="IProviderMapLayerRenderer"/> chain (a WMS entry's tile layer exactly as before, a vector entry's
/// features), and offers the Export GeoJSON / Shapefile and Import GeoJSON actions only to providers with the
/// <see cref="ProviderRenderCapabilities.VectorExport"/> / <see cref="ProviderRenderCapabilities.VectorImport"/>
/// capabilities — a raster-only WMS entry, also behind a decorator, shows none of them.
/// </summary>
public sealed class ProviderPoolPanelRenderingTests : BunitContext
{
    private const string Div = "geoassets-map";

    private readonly ProviderPool _pool = new();
    private readonly LayerRecordingMapInterop _map = new();

    private IRenderedComponent<ProviderPoolPanel> RenderPanel(bool withShapefileExporter = true)
    {
        Services.AddSingleton<IJsonStringLocalizer>(new KeyLocalizer());
        Services.AddSingleton<IProviderPool>(_pool);
        Services.AddSingleton<IMapInterop>(_map);
        Services.AddSingleton<ICurrentMapContext>(new MapContext());
        Services.AddSingleton<FeatureRenderPipeline>();
        Services.AddProviderMapLayerRenderers();
        Services.AddSingleton<IStorageService>(new UnusedStorage());
        Services.AddSingleton<IAnalyticsService>(new UnusedAnalytics());
        Services.AddSingleton<ILogger<ProviderPoolPanel>>(NullLogger<ProviderPoolPanel>.Instance);
        Services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        if (withShapefileExporter) Services.AddSingleton<IShapefileExportService>(new UnusedShapefileExporter());
        ComponentFactories.AddStub<ProviderConnectDialog>();
        return Render<ProviderPoolPanel>();
    }

    private static WmsAssetProvider Wms() => new("https://srv/api/geoassets/wms", "geoassets:feature");

    private static GeoFeature[] Features(int count) =>
        [.. Enumerable.Range(0, count).Select(i => new GeoFeature { Id = $"f-{i}" })];

    [Fact]
    public void AVectorEntry_OffersExportAndImport()
    {
        _pool.Add("Local", new FakeLayerProvider());

        var panel = RenderPanel();

        panel.FindAll(".pool-export-geojson").Should().ContainSingle();
        panel.FindAll(".pool-export-shapefile").Should().ContainSingle();
        panel.FindAll(".pool-import-geojson").Should().ContainSingle();
    }

    [Fact]
    public void AVectorEntry_WithoutAShapefileExporter_OffersNoShapefileExport()
    {
        _pool.Add("Local", new FakeLayerProvider());

        var panel = RenderPanel(withShapefileExporter: false);

        panel.FindAll(".pool-export-geojson").Should().ContainSingle();
        panel.FindAll(".pool-export-shapefile").Should().BeEmpty();
    }

    [Fact]
    public void AWmsEntry_OffersNoVectorAction()
    {
        // Fails before XD01-422: Export GeoJSON and Import were offered to WMS entries too.
        _pool.Add("WMS", Wms());

        var panel = RenderPanel();

        panel.FindAll(".pool-export-geojson").Should().BeEmpty();
        panel.FindAll(".pool-export-shapefile").Should().BeEmpty();
        panel.FindAll(".pool-import-geojson").Should().BeEmpty();
        panel.FindAll("button[title='pool.disable']").Should().ContainSingle("show/hide stays available");
    }

    [Fact]
    public void ADecoratedWmsEntry_OffersNoVectorAction()
    {
        _pool.Add("WMS", new ValidatingAssetProvider(Wms()));

        var panel = RenderPanel();

        panel.FindAll(".pool-export-geojson").Should().BeEmpty();
        panel.FindAll(".pool-import-geojson").Should().BeEmpty();
    }

    [Fact]
    public void AnExportOnlyProvider_OffersExportButNotImport()
    {
        _pool.Add("Read-only", new CapabilityDeclaringLayerProvider(
            ProviderRenderCapabilities.VectorFeatures | ProviderRenderCapabilities.VectorExport));

        var panel = RenderPanel();

        panel.FindAll(".pool-export-geojson").Should().ContainSingle();
        panel.FindAll(".pool-import-geojson").Should().BeEmpty();
    }

    [Fact]
    public void HidingAndShowingAWmsEntry_RemovesAndAddsItsTileLayer()
    {
        var entry = _pool.Add("WMS", Wms());
        var panel = RenderPanel();

        panel.Find("button[title='pool.disable']").Click();

        entry.IsEnabled.Should().BeFalse();
        _map.WmsRemoves.Should().Equal((Div, entry.Id.ToString()));

        panel.Find("button[title='pool.enable']").Click();

        entry.IsEnabled.Should().BeTrue();
        _map.WmsAdds.Should().ContainSingle();
        _map.WmsAdds[0].LayerId.Should().Be(entry.Id.ToString());
        _map.WmsAdds[0].BaseUrl.Should().Be("https://srv/api/geoassets/wms");
        _map.WmsAdds[0].Options.Layers.Should().Be("geoassets:feature");
        _map.WmsAdds[0].Options.Format.Should().Be("image/png");
        _map.RemovedFeatures.Should().BeEmpty();
        _map.FeatureBatches.Should().BeEmpty();
    }

    [Fact]
    public void HidingAndShowingAVectorEntry_RemovesAndRendersItsFeatures()
    {
        var features = Features(2);
        _pool.Add("Local", new FakeLayerProvider(features));
        var panel = RenderPanel();

        panel.Find("button[title='pool.disable']").Click();
        panel.WaitForAssertion(() => _map.RemovedFeatures.Select(r => r.FeatureId).Should().Equal("f-0", "f-1"));

        panel.Find("button[title='pool.enable']").Click();
        panel.WaitForAssertion(() => _map.RenderedFeatureIds.Should().Equal("f-0", "f-1"));
        _map.WmsAdds.Should().BeEmpty();
        _map.WmsRemoves.Should().BeEmpty();
    }

    [Fact]
    public void RemovingAWmsEntry_RemovesItsTileLayerAndTheEntry()
    {
        var entry = _pool.Add("WMS", Wms());
        var panel = RenderPanel();

        panel.Find("button[title='pool.delete']").Click();

        _map.WmsRemoves.Should().Equal((Div, entry.Id.ToString()));
        _pool.All.Should().BeEmpty();
    }

    [Fact]
    public void RemovingAVectorEntry_RemovesItsFeaturesAndTheEntry()
    {
        _pool.Add("Local", new FakeLayerProvider(Features(2)));
        var panel = RenderPanel();

        panel.Find("button[title='pool.delete']").Click();

        panel.WaitForAssertion(() => _pool.All.Should().BeEmpty());
        _map.RemovedFeatures.Select(r => r.FeatureId).Should().Equal("f-0", "f-1");
    }

    [Fact]
    public void CapabilityPredicates_RejectNull()
    {
        FluentActions.Invoking(() => ProviderPoolPanel.CanExportVector(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => ProviderPoolPanel.CanImportVector(null!)).Should().Throw<ArgumentNullException>();
    }

    private sealed class MapContext : ICurrentMapContext
    {
        public string MapDivId => Div;
    }

    private sealed class UnusedStorage : IStorageService
    {
        public Task<GeoFeatureCollection> LoadAsync(string key = "default", CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveAsync(GeoFeatureCollection collection, string key = "default", CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GeoFeatureCollection> ImportFromStringAsync(string geoJson, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> ExportToStringAsync(GeoFeatureCollection collection, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> PickImportFileAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveExportFileAsync(string geoJson, string suggestedName = "export.geojson", CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveExportBinaryFileAsync(byte[] content, string suggestedName, string mimeType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> GetStringAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SetStringAsync(string key, string value, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class UnusedAnalytics : IAnalyticsService
    {
        public void TrackEvent(string name, object? properties = null) => throw new NotSupportedException();
        public void TrackException(string message, object? properties = null) => throw new NotSupportedException();
        public void TrackMetric(string name, double value, object? properties = null) => throw new NotSupportedException();
    }

    private sealed class UnusedShapefileExporter : IShapefileExportService
    {
        public byte[] ExportToZip(IReadOnlyList<GeoFeature> features, string? targetCrsWkt = null, int? targetSrid = null) =>
            throw new NotSupportedException();
    }
}
