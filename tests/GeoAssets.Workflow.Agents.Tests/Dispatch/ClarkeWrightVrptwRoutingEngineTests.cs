using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Agents.Dispatch;
using GeoAssets.Workflow.Dispatch;
using Xunit;

namespace GeoAssets.Workflow.Agents.Tests.Dispatch;

public class ClarkeWrightVrptwRoutingEngineTests
{
    private static readonly GeoPoint Depot = new(-46.64, -23.55);
    private static readonly DateTimeOffset ShiftStart = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Scattered, fully deterministic order locations around <see cref="Depot"/> — a
    /// pseudo-random hash (not <see cref="Random"/>, banned by CA5394 even for non-cryptographic
    /// test data here) spreads <paramref name="count"/> points widely enough that the
    /// Clarke-Wright savings merge reliably beats the naive nearest-neighbor baseline, the
    /// comparison XD01-240's AC asks for.</summary>
    private static List<DispatchableOrder> ScatteredOrders(int count)
    {
        var orders = new List<DispatchableOrder>(count);
        for (var i = 0; i < count; i++)
        {
            var lon = Depot.Longitude + (DeterministicUnitHash(i, 1) - 0.5) * 0.2;
            var lat = Depot.Latitude + (DeterministicUnitHash(i, 2) - 0.5) * 0.2;
            orders.Add(new DispatchableOrder(
                $"order-{i}", new GeoPoint(lon, lat),
                new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal),
                TimeSpan.FromMinutes(20)));
        }
        return orders;
    }

    /// <summary>Classic sine-hash pseudo-random generator — deterministic, no
    /// <see cref="Random"/> instance, result in [0, 1).</summary>
    private static double DeterministicUnitHash(int index, int salt)
    {
        var x = Math.Sin((index * 12.9898) + (salt * 78.233)) * 43758.5453;
        return x - Math.Floor(x);
    }

    [Fact]
    public void BuildItinerary_ScatteredOrders_ProducesShorterRouteThanNaiveNearestNeighbor()
    {
        var orders = ScatteredOrders(14);
        var engine = new ClarkeWrightVrptwRoutingEngine();

        var itinerary = engine.BuildItinerary("crew-1", Depot, orders, ShiftStart);
        var naiveDistance = NearestNeighborBaselineRouter.ComputeTotalDistanceMeters(Depot, orders);

        itinerary.Stops.Should().HaveCount(orders.Count, "every order must appear exactly once in the itinerary");
        itinerary.Stops.Select(s => s.OrderId).Should().BeEquivalentTo(orders.Select(o => o.OrderId));
        itinerary.TotalDistanceMeters.Should().BeLessThan(naiveDistance);
    }

    [Fact]
    public void BuildItinerary_SingleOrder_ReturnsOneStopAndRoundTripDistance()
    {
        var order = new DispatchableOrder(
            "order-1", new GeoPoint(Depot.Longitude + 0.01, Depot.Latitude),
            new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal),
            TimeSpan.FromMinutes(20));
        var engine = new ClarkeWrightVrptwRoutingEngine();

        var itinerary = engine.BuildItinerary("crew-1", Depot, [order], ShiftStart);

        itinerary.Stops.Should().ContainSingle();
        itinerary.Stops[0].OrderId.Should().Be("order-1");
        itinerary.TotalDistanceMeters.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BuildItinerary_NoOrders_ReturnsEmptyItinerary()
    {
        var engine = new ClarkeWrightVrptwRoutingEngine();

        var itinerary = engine.BuildItinerary("crew-1", Depot, [], ShiftStart);

        itinerary.Stops.Should().BeEmpty();
        itinerary.TotalDistanceMeters.Should().Be(0);
    }

    [Fact]
    public void BuildItinerary_TightTimeWindow_StillVisitsEveryOrderExactlyOnce()
    {
        // An unreachable-in-time window for one far stop forces the savings merge pass to skip
        // that merge — the leftover-fragment join pass must still produce a complete sequence.
        var orders = ScatteredOrders(6);
        orders[^1] = orders[^1] with { TimeWindowEnd = ShiftStart.AddMinutes(1) };
        var engine = new ClarkeWrightVrptwRoutingEngine();

        var itinerary = engine.BuildItinerary("crew-1", Depot, orders, ShiftStart);

        itinerary.Stops.Select(s => s.OrderId).Should().BeEquivalentTo(orders.Select(o => o.OrderId));
    }
}
