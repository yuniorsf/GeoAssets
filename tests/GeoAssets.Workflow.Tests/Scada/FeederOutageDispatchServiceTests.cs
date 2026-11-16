using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Scada;
using GeoAssets.Workflow.Tests.Orders;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Scada;

public class FeederOutageDispatchServiceTests
{
    private static GeoFeature Switch(string id, Guid orgId = default) => new()
    {
        Id = id,
        Properties = new GeoFeatureProperties { Name = $"Switch {id}", AssetTypeId = "switch", OrganizationId = orgId },
        Geometry = new GeoPoint(-43.2, -22.9),
    };

    private static GeoFeature Downstream(string id, string parentId, bool critical = false) => new()
    {
        Id = id,
        Properties = new GeoFeatureProperties
        {
            Name = $"Feature {id}",
            AssetTypeId = "consumer-unit",
            CustomAttributes = critical
                ? new Dictionary<string, string>(StringComparer.Ordinal) { [ScadaOutageOrderTypes.CriticalFacilityAttributeKey] = "Healthcare" }
                : new Dictionary<string, string>(StringComparer.Ordinal),
        },
    };

    private static GeoFeature Crew(string id, bool qualified, double lon = -43.2, double lat = -22.9) => new()
    {
        Id = id,
        Properties = new GeoFeatureProperties
        {
            Name = $"Crew {id}",
            AssetTypeId = ScadaOutageOrderTypes.CrewAssetTypeId,
            CustomAttributes = qualified
                ? new Dictionary<string, string>(StringComparer.Ordinal) { [ScadaOutageOrderTypes.EmergencyQualifiedAttributeKey] = "true" }
                : new Dictionary<string, string>(StringComparer.Ordinal),
        },
        Geometry = new GeoPoint(lon, lat),
    };

    private static (FeederOutageDispatchService Service, TestAssetProvider Provider, FakeServiceOrderRepository Repo, FakeTimeProvider Time) Build()
    {
        var provider = new TestAssetProvider();
        var repo = new FakeServiceOrderRepository();
        var time = new FakeTimeProvider();
        return (new FeederOutageDispatchService(provider, repo, time), provider, repo, time);
    }

    [Fact]
    public Task DispatchForBreakerTripAsync_UnknownSwitch_Throws()
    {
        var (service, _, _, _) = Build();

        var act = () => service.DispatchForBreakerTripAsync("missing");

        return act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DispatchForBreakerTripAsync_EnumeratesDownstreamImpactAndFlagsCriticalFacility()
    {
        var (service, provider, repo, _) = Build();

        var sw = Switch("sw1");
        provider.Add(sw);
        sw.Topology.Add(new TopoEdge { TargetId = "d1", Kind = ScadaOutageOrderTypes.FeedsEdgeKind });
        sw.Topology.Add(new TopoEdge { TargetId = "d2", Kind = ScadaOutageOrderTypes.FeedsEdgeKind });
        provider.Add(Downstream("d1", "sw1"));
        provider.Add(Downstream("d2", "sw1", critical: true));
        // A sibling edge of a different kind must NOT be included.
        sw.Topology.Add(new TopoEdge { TargetId = "d3", Kind = "connected-to" });
        provider.Add(Downstream("d3", "sw1"));

        var result = await service.DispatchForBreakerTripAsync("sw1");

        result.ImpactedFeatureIds.Should().BeEquivalentTo(["d1", "d2"]);
        result.CriticalFacilityFeatureIds.Should().BeEquivalentTo(["d2"]);

        var order = await repo.GetByIdAsync(result.ServiceOrderId);
        order.Should().NotBeNull();
        order!.Priority.Should().Be(ServiceOrderPriority.Critical);
        order.OrderTypeId.Should().Be(ScadaOutageOrderTypes.EmergencyOutageDispatchId);
        order.Attributes[ScadaOutageOrderTypes.TrippedSwitchAttributeKey].Should().Be("sw1");
        order.Attributes[ScadaOutageOrderTypes.CriticalFacilityCountAttributeKey].Should().Be("1");
    }

    [Fact]
    public async Task DispatchForBreakerTripAsync_DispatchesToNearestQualifiedCrewOnly()
    {
        var (service, provider, repo, _) = Build();

        var sw = Switch("sw1");
        provider.Add(sw);
        provider.Add(Crew("unqualified-near", qualified: false, lon: -43.2001, lat: -22.9001));
        provider.Add(Crew("qualified-far", qualified: true, lon: -43.5, lat: -23.2));
        provider.Add(Crew("qualified-near", qualified: true, lon: -43.2002, lat: -22.9002));

        var result = await service.DispatchForBreakerTripAsync("sw1");

        result.DispatchedCrewFeatureId.Should().Be("qualified-near");

        var order = await repo.GetByIdAsync(result.ServiceOrderId);
        order!.Dispatches.Should().ContainSingle(d =>
            d.TargetId == "qualified-near" &&
            d.TargetType == DispatchTargetType.Group &&
            d.ActorKind == ActorKind.System);
    }

    [Fact]
    public async Task DispatchForBreakerTripAsync_NoQualifiedCrewNearby_StillCreatesOrderUndispatched()
    {
        var (service, provider, repo, _) = Build();
        provider.Add(Switch("sw1"));

        var result = await service.DispatchForBreakerTripAsync("sw1");

        result.DispatchedCrewFeatureId.Should().BeNull();
        var order = await repo.GetByIdAsync(result.ServiceOrderId);
        order!.Dispatches.Should().BeEmpty();
        order.Status.Should().NotBeNull();
    }
}
