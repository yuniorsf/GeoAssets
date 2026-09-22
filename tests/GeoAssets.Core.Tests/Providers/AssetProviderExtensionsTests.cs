using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Services;
using GeoAssets.Core.Tests;
using Xunit;

namespace GeoAssets.Core.Tests.Providers;

/// <summary>
/// Exercises <see cref="AssetProviderExtensions.UnwrapToConcrete"/>'s decorator-chain walk (XD01-160)
/// using a minimal <see cref="IProviderDecorator"/> fake — <see cref="ValidatingAssetProviderTests"/>
/// and <see cref="ActiveAssetProviderTests"/> separately verify each real decorator's own
/// <c>Inner</c> implementation; <c>ObservableAssetProviderTests</c> (GeoAssets.Shared.Tests) verifies
/// the full real 3-layer production chain end-to-end.
/// </summary>
public class AssetProviderExtensionsTests
{
    /// <summary>Wraps an <see cref="IAssetProvider"/> without doing anything else — every other
    /// member throws, since <c>UnwrapToConcrete</c> never calls them.</summary>
    private sealed class FakeDecorator(IAssetProvider inner) : IAssetProvider, IProviderDecorator
    {
        public IAssetProvider Inner => inner;

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

    [Fact]
    public void UnwrapToConcrete_UndecoratedProvider_ReturnsSameInstance()
    {
        var concrete = new TestAssetProvider();

        ((IAssetProvider)concrete).UnwrapToConcrete().Should().BeSameAs(concrete);
    }

    [Fact]
    public void UnwrapToConcrete_OneLayer_ReturnsConcreteProvider()
    {
        var concrete = new TestAssetProvider();
        var decorated = new FakeDecorator(concrete);

        decorated.UnwrapToConcrete().Should().BeSameAs(concrete);
    }

    [Fact]
    public void UnwrapToConcrete_ThreeLayers_ReturnsConcreteProvider()
    {
        var concrete = new TestAssetProvider();
        var layer1 = new FakeDecorator(concrete);
        var layer2 = new FakeDecorator(layer1);
        var layer3 = new FakeDecorator(layer2);

        layer3.UnwrapToConcrete().Should().BeSameAs(concrete);
    }
}
