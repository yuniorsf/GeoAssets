using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Orders;
using GeoAssets.Integrations.Scada;
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

        var order = await repo.GetByIdAsync(result.ServiceOrderId!);
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

        var order = await repo.GetByIdAsync(result.ServiceOrderId!);
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
        var order = await repo.GetByIdAsync(result.ServiceOrderId!);
        order!.Dispatches.Should().BeEmpty();
        order.Status.Should().NotBeNull();
    }

    // ── Degraded outcomes when the core asset source is unreachable (XD01-386) ──

    private static FeederOutageDispatchService BuildWith(FlakyTopologyQuery query, FakeServiceOrderRepository repo) =>
        new(query, repo, new FakeTimeProvider());

    private static FlakyTopologyQuery SeededQuery()
    {
        var provider = new TestAssetProvider();
        var sw = Switch("sw1");
        provider.Add(sw);
        sw.Topology.Add(new TopoEdge { TargetId = "d1", Kind = ScadaOutageOrderTypes.FeedsEdgeKind });
        provider.Add(Downstream("d1", "sw1"));
        provider.Add(Crew("qualified-near", qualified: true, lon: -43.2002, lat: -22.9002));
        return new FlakyTopologyQuery(provider);
    }

    [Fact]
    public async Task DispatchForBreakerTripAsync_HappyPath_IsNotDegraded()
    {
        var repo = new FakeServiceOrderRepository();
        var service = BuildWith(SeededQuery(), repo);

        var result = await service.DispatchForBreakerTripAsync("sw1");

        result.IsDegraded.Should().BeFalse();
        result.DegradedReason.Should().BeNull();
        result.ServiceOrderId.Should().NotBeNull();
        result.ImpactedFeatureIds.Should().Equal("d1");
        result.DispatchedCrewFeatureId.Should().Be("qualified-near");
    }

    [Fact]
    public async Task DispatchForBreakerTripAsync_SwitchUnreadable_CreatesNoOrderAndIsDegraded()
    {
        var repo = new FakeServiceOrderRepository();
        var query = SeededQuery();
        query.SwitchFailure = new GeoCoreUnavailableException("core down");
        var service = BuildWith(query, repo);

        var result = await service.DispatchForBreakerTripAsync("sw1");

        result.IsDegraded.Should().BeTrue();
        result.ServiceOrderId.Should().BeNull();
        result.TrippedSwitchFeatureId.Should().Be("sw1");
        result.ImpactedFeatureIds.Should().BeEmpty();
        result.DispatchedCrewFeatureId.Should().BeNull();
        result.DegradedReason.Should().Contain("Tripped switch could not be read").And.Contain("core down");
        (await repo.GetAllAsync()).Should().BeEmpty("without the switch there is nothing to build the order from");
        query.DescendantCalls.Should().Be(0);
        query.CrewCalls.Should().Be(0);
    }

    [Fact]
    public async Task DispatchForBreakerTripAsync_DescendantsUnreadable_CreatesOrderWithoutImpactAndIsDegraded()
    {
        var repo = new FakeServiceOrderRepository();
        var query = SeededQuery();
        query.DescendantsFailure = new GeoCoreUnavailableException("topology down");
        var service = BuildWith(query, repo);

        var result = await service.DispatchForBreakerTripAsync("sw1");

        result.IsDegraded.Should().BeTrue();
        result.ServiceOrderId.Should().NotBeNull();
        result.ImpactedFeatureIds.Should().BeEmpty();
        result.CriticalFacilityFeatureIds.Should().BeEmpty();
        result.DispatchedCrewFeatureId.Should().Be("qualified-near", "the crew lookup is independent of the downstream traversal");
        result.DegradedReason.Should().Contain("Downstream impact could not be resolved").And.Contain("topology down");
        var order = await repo.GetByIdAsync(result.ServiceOrderId!);
        order.Should().NotBeNull();
        order!.Description.Should().Contain("could not be resolved");
    }

    [Fact]
    public async Task DispatchForBreakerTripAsync_CrewLookupFails_CreatesUndispatchedOrderAndIsDegraded()
    {
        var repo = new FakeServiceOrderRepository();
        var query = SeededQuery();
        query.CrewFailure = new GeoCoreUnavailableException("crew roster down");
        var service = BuildWith(query, repo);

        var result = await service.DispatchForBreakerTripAsync("sw1");

        result.IsDegraded.Should().BeTrue();
        result.ServiceOrderId.Should().NotBeNull();
        result.ImpactedFeatureIds.Should().Equal("d1");
        result.DispatchedCrewFeatureId.Should().BeNull();
        result.DegradedReason.Should().Contain("left undispatched").And.Contain("crew roster down");
        var order = await repo.GetByIdAsync(result.ServiceOrderId!);
        order!.Dispatches.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchForBreakerTripAsync_DescendantsAndCrewBothUnreadable_ReportsBothInOneDegradedResult()
    {
        var repo = new FakeServiceOrderRepository();
        var query = SeededQuery();
        query.DescendantsFailure = new GeoCoreUnavailableException("topology down");
        query.CrewFailure = new HttpRequestException("crew down");
        var service = BuildWith(query, repo);

        var result = await service.DispatchForBreakerTripAsync("sw1");

        result.IsDegraded.Should().BeTrue();
        result.ServiceOrderId.Should().NotBeNull();
        result.DegradedReason.Should().Contain("topology down").And.Contain("crew down");
    }

    [Fact]
    public async Task DispatchForBreakerTripAsync_RawHttpRequestExceptionOnSwitch_IsTreatedAsUnreachable()
    {
        var repo = new FakeServiceOrderRepository();
        var query = SeededQuery();
        query.SwitchFailure = new HttpRequestException("connection refused");
        var service = BuildWith(query, repo);

        var result = await service.DispatchForBreakerTripAsync("sw1");

        result.IsDegraded.Should().BeTrue();
        result.ServiceOrderId.Should().BeNull();
    }

    [Fact]
    public async Task DispatchForBreakerTripAsync_TimeoutNotFromCaller_IsTreatedAsUnreachable()
    {
        var repo = new FakeServiceOrderRepository();
        var query = SeededQuery();
        query.DescendantsFailure = new TaskCanceledException("timed out");
        var service = BuildWith(query, repo);

        var result = await service.DispatchForBreakerTripAsync("sw1");

        result.IsDegraded.Should().BeTrue();
        result.ImpactedFeatureIds.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatchForBreakerTripAsync_CallerCancels_PropagatesAndCreatesNoOrder()
    {
        var repo = new FakeServiceOrderRepository();
        var query = SeededQuery();
        query.SwitchFailure = new TaskCanceledException("cancelled");
        var service = BuildWith(query, repo);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.DispatchForBreakerTripAsync("sw1", ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await repo.GetAllAsync()).Should().BeEmpty();
    }

    [Fact]
    public Task DispatchForBreakerTripAsync_SwitchNotFoundInHealthyCore_ThrowsKeyNotFoundNotDegraded()
    {
        var repo = new FakeServiceOrderRepository();
        var service = BuildWith(new FlakyTopologyQuery(new TestAssetProvider()), repo);

        var act = () => service.DispatchForBreakerTripAsync("missing");

        return act.Should().ThrowAsync<KeyNotFoundException>();
    }

    /// <summary>Delegates to an in-process store but fails each read on demand.</summary>
    private sealed class FlakyTopologyQuery(TestAssetProvider inner) : IAssetTopologyQuery
    {
        public Exception? SwitchFailure { get; set; }

        public Exception? DescendantsFailure { get; set; }

        public Exception? CrewFailure { get; set; }

        public int DescendantCalls { get; private set; }

        public int CrewCalls { get; private set; }

        public Task<GeoFeature?> GetFeatureAsync(string id, CancellationToken ct) =>
            SwitchFailure is { } failure ? Task.FromException<GeoFeature?>(failure) : ((IAssetTopologyQuery)inner).GetFeatureAsync(id, ct);

        public Task<IReadOnlyList<GeoFeature>> GetDescendantsAsync(string id, string edgeKind, CancellationToken ct)
        {
            DescendantCalls++;
            return DescendantsFailure is { } failure
                ? Task.FromException<IReadOnlyList<GeoFeature>>(failure)
                : ((IAssetTopologyQuery)inner).GetDescendantsAsync(id, edgeKind, ct);
        }

        public Task<IReadOnlyList<GeoFeature>> GetFeaturesByAssetTypeAsync(string assetTypeId, CancellationToken ct)
        {
            CrewCalls++;
            return CrewFailure is { } failure
                ? Task.FromException<IReadOnlyList<GeoFeature>>(failure)
                : ((IAssetTopologyQuery)inner).GetFeaturesByAssetTypeAsync(assetTypeId, ct);
        }
    }
}
