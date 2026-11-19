using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.FieldService.FieldWork;
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
    public Task CanArriveAsync_ProximityPortUnavailable_SurfacesGeoCoreUnavailableException()
    {
        var guard = new GeofenceArrivalTransitionGuard(new StubProximityQuery(
            (_, _, _, _) => Task.FromException<bool>(new GeoCoreUnavailableException("core down"))));
        var order = OrderWithFeature("asset-1");

        var act = () => guard.CanArriveAsync(order, AssetLocation);

        return act.Should().ThrowAsync<GeoCoreUnavailableException>().WithMessage("core down");
    }

    [Fact]
    public async Task CanArriveAsync_ForwardsFeatureIdPointToleranceAndCancellationToken()
    {
        string? seenFeatureId = null;
        GeoPoint? seenPoint = null;
        double seenTolerance = 0;
        CancellationToken seenToken = default;
        var guard = new GeofenceArrivalTransitionGuard(new StubProximityQuery((featureId, point, tolerance, token) =>
        {
            seenFeatureId = featureId;
            seenPoint = point;
            seenTolerance = tolerance;
            seenToken = token;
            return Task.FromResult(true);
        }));
        using var cts = new CancellationTokenSource();

        var result = await guard.CanArriveAsync(OrderWithFeature("asset-1"), AssetLocation, cts.Token);

        result.Should().BeTrue();
        seenFeatureId.Should().Be("asset-1");
        seenPoint.Should().BeSameAs(AssetLocation);
        seenTolerance.Should().Be(GeofenceArrivalTransitionGuard.ToleranceMeters);
        seenToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task CanArriveAsync_OrderHasNoFeatures_DoesNotConsultTheProximityPort()
    {
        var calls = 0;
        var guard = new GeofenceArrivalTransitionGuard(new StubProximityQuery((_, _, _, _) =>
        {
            calls++;
            return Task.FromResult(true);
        }));

        var result = await guard.CanArriveAsync(new ServiceOrder(), AssetLocation);

        result.Should().BeFalse();
        calls.Should().Be(0);
    }

    [Fact]
    public async Task CanArriveAsync_FeaturesNotHydrated_UsesFirstPersistedFeatureId()
    {
        string? asked = null;
        var guard = new GeofenceArrivalTransitionGuard(new StubProximityQuery((featureId, _, _, _) =>
        {
            asked = featureId;
            return Task.FromResult(true);
        }));
        var order = new ServiceOrder { FeatureIds = ["asset-7", "asset-8"] };

        var result = await guard.CanArriveAsync(order, AssetLocation);

        result.Should().BeTrue();
        asked.Should().Be("asset-7");
    }

    [Fact]
    public async Task CanArriveAsync_HydratedFeaturesPresent_PreferredOverFeatureIds()
    {
        string? asked = null;
        var guard = new GeofenceArrivalTransitionGuard(new StubProximityQuery((featureId, _, _, _) =>
        {
            asked = featureId;
            return Task.FromResult(true);
        }));
        var order = OrderWithFeature("asset-1");
        order.FeatureIds = ["asset-9"];

        await guard.CanArriveAsync(order, AssetLocation);

        asked.Should().Be("asset-1");
    }

    [Fact]
    public async Task CanStartWorkAsync_AlwaysReturnsTrue_PendingXD01_222()
    {
        var guard = new GeofenceArrivalTransitionGuard(new TestAssetProvider());

        (await guard.CanStartWorkAsync(new ServiceOrder())).Should().BeTrue();
    }

    private sealed class StubProximityQuery(Func<string, GeoPoint, double, CancellationToken, Task<bool>> answer) : IAssetProximityQuery
    {
        public Task<bool> IsWithinDistanceAsync(string featureId, GeoPoint point, double toleranceMeters, CancellationToken ct) =>
            answer(featureId, point, toleranceMeters, ct);
    }
}
