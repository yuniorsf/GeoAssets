using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

public class GeofenceArrivalTransitionGuardTests
{
    private static readonly GeoPoint AssetLocation = new(-46.633, -23.55);

    private static ServiceOrder OrderWithFeature(string featureId) => new()
    {
        Features = [new GeoFeature { Id = featureId, Geometry = AssetLocation }],
    };

    [Fact]
    public async Task CanArriveAsync_DeviceWithinTolerance_ReturnsTrue()
    {
        var assetProvider = new TestAssetProvider();
        assetProvider.Add(new GeoFeature { Id = "asset-1", Geometry = AssetLocation });
        var guard = new GeofenceArrivalTransitionGuard(assetProvider);
        var order = OrderWithFeature("asset-1");

        var result = await guard.CanArriveAsync(order, AssetLocation);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanArriveAsync_DeviceFarFromAsset_ReturnsFalse()
    {
        var assetProvider = new TestAssetProvider();
        assetProvider.Add(new GeoFeature { Id = "asset-1", Geometry = AssetLocation });
        var guard = new GeofenceArrivalTransitionGuard(assetProvider);
        var order = OrderWithFeature("asset-1");

        var farAway = new GeoPoint(AssetLocation.Longitude, AssetLocation.Latitude + 1);
        var result = await guard.CanArriveAsync(order, farAway);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanArriveAsync_OrderHasNoFeatures_ReturnsFalse()
    {
        var assetProvider = new TestAssetProvider();
        var guard = new GeofenceArrivalTransitionGuard(assetProvider);
        var order = new ServiceOrder();

        var result = await guard.CanArriveAsync(order, AssetLocation);

        result.Should().BeFalse("there is no target asset to confirm arrival at");
    }

    [Fact]
    public async Task CanStartWorkAsync_AlwaysReturnsTrue_PendingXD01_222()
    {
        var guard = new GeofenceArrivalTransitionGuard(new TestAssetProvider());

        (await guard.CanStartWorkAsync(new ServiceOrder())).Should().BeTrue();
    }
}
