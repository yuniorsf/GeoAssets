using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Providers;

/// <summary>
/// XD01-422 — <see cref="AssetProviderExtensions.GetRenderCapabilities"/>: a declared
/// <see cref="IProviderCapabilities"/> wins, an undeclared <see cref="IWmsProvider"/> reads as raster tiles, anything
/// else as <see cref="AssetProviderExtensions.DefaultVectorCapabilities"/> — and always for the concrete provider
/// under the decorator chain (the decorators implement neither interface, which is the bug a plain
/// <c>is IWmsProvider</c> on the decorated provider had).
/// </summary>
public sealed class AssetProviderRenderCapabilitiesTests
{
    [Fact]
    public void AnUndeclaredProvider_IsReadAsVectorWithEveryVectorAction()
    {
        new TestAssetProvider().GetRenderCapabilities().Should().Be(
            ProviderRenderCapabilities.VectorFeatures | ProviderRenderCapabilities.VectorExport
            | ProviderRenderCapabilities.VectorImport | ProviderRenderCapabilities.InteractiveEditing);
    }

    [Fact]
    public void AnUndeclaredWmsProvider_IsReadAsRasterTilesOnly()
    {
        new UndeclaredWms().GetRenderCapabilities().Should().Be(ProviderRenderCapabilities.RasterTiles);
    }

    [Fact]
    public void DeclaredCapabilities_WinOverTheWmsFallback()
    {
        var declared = ProviderRenderCapabilities.RasterTiles | ProviderRenderCapabilities.VectorExport;

        new DeclaringWms(declared).GetRenderCapabilities().Should().Be(declared);
    }

    [Fact]
    public void DeclaredNone_IsNone()
    {
        new DeclaringWms(ProviderRenderCapabilities.None).GetRenderCapabilities().Should().Be(ProviderRenderCapabilities.None);
    }

    [Fact]
    public void AWmsProviderBehindValidatingAssetProvider_IsStillRasterTiles()
    {
        IAssetProvider decorated = new ValidatingAssetProvider(new UndeclaredWms());

        (decorated is IWmsProvider).Should().BeFalse("the decorator hides the WMS provider from a plain type test");
        decorated.GetRenderCapabilities().Should().Be(ProviderRenderCapabilities.RasterTiles);
    }

    [Fact]
    public void DeclaredCapabilitiesBehindSeveralDecorators_AreTheConcreteProvidersOwn()
    {
        var declared = ProviderRenderCapabilities.VectorTiles | ProviderRenderCapabilities.HybridLod;

        new ValidatingAssetProvider(new ValidatingAssetProvider(new DeclaringWms(declared)))
            .GetRenderCapabilities().Should().Be(declared);
    }

    [Fact]
    public void TheActiveProviderProxy_ReadsTheActiveEntrysProvider()
    {
        var pool = new ProviderPool();
        var active = new ActiveAssetProvider(pool);
        var wms = pool.Add("WMS", new UndeclaredWms());

        pool.SetActive(wms.Id);

        active.GetRenderCapabilities().Should().Be(ProviderRenderCapabilities.RasterTiles);
    }

    [Fact]
    public void Null_Throws()
    {
        var act = () => AssetProviderExtensions.GetRenderCapabilities(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>A WMS provider that doesn't declare capabilities — the pre-XD01-422 shape of every WMS provider.</summary>
    private class UndeclaredWms : AssetProviderStub, IWmsProvider
    {
        public string WmsBaseUrl => "https://srv/wms";
        public string WmsLayerName => "geoassets:feature";
        public string WmsFormat => "image/png";
    }

    private sealed class DeclaringWms(ProviderRenderCapabilities capabilities) : UndeclaredWms, IProviderCapabilities
    {
        public ProviderRenderCapabilities RenderCapabilities => capabilities;
    }

    /// <summary>Every member throws — <c>GetRenderCapabilities</c> reads none of them.</summary>
    private abstract class AssetProviderStub : IAssetProvider
    {
        public GeoFeature? GetById(string id) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetAll() => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> Search(string query) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) => throw new NotSupportedException();
        public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) => throw new NotSupportedException();
        public Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat) => throw new NotSupportedException();
        public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) => throw new NotSupportedException();
        public void Add(GeoFeature feature) => throw new NotSupportedException();
        public void Update(GeoFeature feature) => throw new NotSupportedException();
        public void AddRange(IEnumerable<GeoFeature> features) => throw new NotSupportedException();
        public void Delete(string id) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void LoadAll(IEnumerable<GeoFeature> features) => throw new NotSupportedException();
        public IReadOnlyList<AssetType> GetAssetTypes() => throw new NotSupportedException();
        public void AddAssetType(AssetType assetType) => throw new NotSupportedException();
        public void DeleteAssetType(Guid id) => throw new NotSupportedException();
        public IReadOnlyList<Layer> GetLayers() => throw new NotSupportedException();
        public void AddLayer(Layer layer) => throw new NotSupportedException();
        public void DeleteLayer(Guid id) => throw new NotSupportedException();
        public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) => throw new NotSupportedException();
        public void AddLayerRule(LayerRule layerRule) => throw new NotSupportedException();
        public void DeleteLayerRule(Guid id) => throw new NotSupportedException();

        public event EventHandler<GeoFeature>? FeatureAdded { add { } remove { } }
        public event EventHandler<GeoFeature>? FeatureUpdated { add { } remove { } }
        public event EventHandler<string>? FeatureDeleted { add { } remove { } }
        public event EventHandler? CollectionChanged { add { } remove { } }
    }
}
