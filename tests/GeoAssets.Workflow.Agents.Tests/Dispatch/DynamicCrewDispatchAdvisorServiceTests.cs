using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Agents.Dispatch;
using GeoAssets.Workflow.Agents.Tests.TestDoubles;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Dispatch;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.SlaGuard;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Agents.Tests.Dispatch;

/// <summary>
/// XD01-256's AC: VRPTW-routed suggestions measurably shorter than a naive baseline, yield
/// ranking by predicted US value, SLA deadlines never deprioritized, and the result is purely
/// advisory (the service itself never calls a dispatch executor — there is deliberately no
/// executor dependency to assert against here).
/// </summary>
public class DynamicCrewDispatchAdvisorServiceTests
{
    private static readonly Guid OrgId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime CatalogAsOf = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FakeUsCatalogRepository : IUsCatalogRepository
    {
        private readonly List<UsCatalogItem> _items = [];

        public void Seed(UsCatalogItem item) => _items.Add(item);

        public Task<UsCatalogItem?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_items.Find(i => i.Id == id));

        public Task<UsCatalogItem?> FindEffectiveAsync(Guid organizationId, string itemCode, DateTime asOfUtc, CancellationToken ct = default) =>
            Task.FromResult(_items.Find(i =>
                i.OrganizationId == organizationId
                && string.Equals(i.ItemCode, itemCode, StringComparison.Ordinal)
                && i.EffectiveFrom <= asOfUtc
                && (i.EffectiveTo is null || i.EffectiveTo > asOfUtc)));

        public Task<IReadOnlyList<UsCatalogItem>> GetByOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<UsCatalogItem>>(_items.FindAll(i => i.OrganizationId == organizationId));

        public Task AddAsync(UsCatalogItem item, CancellationToken ct = default) { _items.Add(item); return Task.CompletedTask; }
        public Task UpdateAsync(UsCatalogItem item, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken ct = default) { _items.RemoveAll(i => i.Id == id); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static UsCatalogItem CatalogItem(string itemCode, decimal baseUsFactor) => new()
    {
        OrganizationId = OrgId,
        ItemCode = itemCode,
        BaseUsFactor = baseUsFactor,
        RegionalMultiplier = 1m,
        MonetaryConversionRate = 1m,
        EffectiveFrom = CatalogAsOf,
    };

    private static GeoFeature CrewFeature(string id, GeoPoint depot) => new()
    {
        Id = id,
        Geometry = depot,
        Properties = new GeoFeatureProperties { AssetTypeId = FeederClusterDispatchTypes.RoutineCrewAssetTypeId },
    };

    private static GeoFeature PoleFeature(string id, GeoPoint location) => new()
    {
        Id = id,
        Geometry = location,
        Properties = new GeoFeatureProperties { AssetTypeId = "pole" },
    };

    private static ServiceOrder DraftOrder(string id, GeoFeature feature, string? boqJson = null, DateTimeOffset? slaDeadline = null)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (boqJson is not null) attributes[ServiceOrderBoqAttributes.BoqLinesKey] = boqJson;
        if (slaDeadline is not null) attributes[SlaGuardEngine.DeadlineAttributeKey] = slaDeadline.Value.ToString("O");

        return new ServiceOrder
        {
            Id = id,
            Title = $"Order {id}",
            OrderTypeId = "routine",
            Status = ServiceOrderStatus.Draft,
            OrganizationId = OrgId,
            Features = [feature],
            Attributes = attributes,
        };
    }

    private static async Task<(DynamicCrewDispatchAdvisorService Service, TestAssetProvider AssetProvider, SnapshottingServiceOrderRepository Orders, FakeUsCatalogRepository Catalog)>
        CreateAsync(DateTimeOffset now)
    {
        var assetProvider = new TestAssetProvider();
        var orderRepository = new SnapshottingServiceOrderRepository();
        var catalog = new FakeUsCatalogRepository();
        catalog.Seed(CatalogItem("US-EL-014", baseUsFactor: 10m));

        var timeProvider = new FakeTimeProvider(now);
        var remuneration = new OrderRemunerationCalculator(catalog);
        var routingEngine = new ClarkeWrightVrptwRoutingEngine();

        var service = new DynamicCrewDispatchAdvisorService(assetProvider, orderRepository, routingEngine, remuneration, timeProvider);

        return await Task.FromResult((service, assetProvider, orderRepository, catalog));
    }

    [Fact]
    public async Task GenerateAdvisoryPlanAsync_RanksHighValueOrderAheadOfLowValueOrder()
    {
        var now = new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);
        var (service, assets, orders, _) = await CreateAsync(now);

        var depot = new GeoPoint(-46.00, -23.00);
        assets.Add(CrewFeature("crew-1", depot));
        var poleLow = PoleFeature("pole-low", new GeoPoint(-46.01, -23.01));
        var poleHigh = PoleFeature("pole-high", new GeoPoint(-46.02, -23.02));
        assets.Add(poleLow);
        assets.Add(poleHigh);

        await orders.AddAsync(DraftOrder("order-low", poleLow, boqJson: """[{"itemCode":"US-EL-014","quantity":1}]"""));
        await orders.AddAsync(DraftOrder("order-high", poleHigh, boqJson: """[{"itemCode":"US-EL-014","quantity":100}]"""));

        var result = await service.GenerateAdvisoryPlanAsync();

        result.RankedOrderIds.Should().Equal("order-high", "order-low");
    }

    [Fact]
    public async Task GenerateAdvisoryPlanAsync_UrgentLowValueOrder_OutranksNonUrgentHighValueOrder()
    {
        var now = new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);
        var (service, assets, orders, _) = await CreateAsync(now);

        var depot = new GeoPoint(-46.00, -23.00);
        assets.Add(CrewFeature("crew-1", depot));
        var poleUrgent = PoleFeature("pole-urgent", new GeoPoint(-46.01, -23.01));
        var poleValuable = PoleFeature("pole-valuable", new GeoPoint(-46.02, -23.02));
        assets.Add(poleUrgent);
        assets.Add(poleValuable);

        await orders.AddAsync(DraftOrder("order-urgent", poleUrgent, slaDeadline: now.AddHours(2)));
        await orders.AddAsync(DraftOrder("order-valuable", poleValuable, boqJson: """[{"itemCode":"US-EL-014","quantity":1000}]"""));

        var result = await service.GenerateAdvisoryPlanAsync();

        result.RankedOrderIds.Should().Equal("order-urgent", "order-valuable");
        result.DeadlineCriticalOrderIds.Should().ContainSingle().Which.Should().Be("order-urgent");
    }

    [Fact]
    public async Task GenerateAdvisoryPlanAsync_RoutedItinerary_IsNeverLongerThanNaiveBaseline()
    {
        var now = new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);
        var (service, assets, orders, _) = await CreateAsync(now);

        var depot = new GeoPoint(-46.00, -23.00);
        assets.Add(CrewFeature("crew-1", depot));
        assets.Add(PoleFeature("pole-a", new GeoPoint(-46.01, -23.00)));
        assets.Add(PoleFeature("pole-b", new GeoPoint(-46.05, -23.00)));
        assets.Add(PoleFeature("pole-c", new GeoPoint(-46.02, -23.00)));

        await orders.AddAsync(DraftOrder("order-a", assets.GetById("pole-a")!));
        await orders.AddAsync(DraftOrder("order-b", assets.GetById("pole-b")!));
        await orders.AddAsync(DraftOrder("order-c", assets.GetById("pole-c")!));

        var result = await service.GenerateAdvisoryPlanAsync();

        result.OptimizedDistanceMeters.Should().BeLessOrEqualTo(result.BaselineDistanceMeters);
        result.SuggestedItineraries.Should().ContainSingle();
        result.UnassignedOrderIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAdvisoryPlanAsync_OrderWithNoResolvableFeature_IsSkippedNotThrown()
    {
        var now = new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);
        var (service, assets, orders, _) = await CreateAsync(now);

        assets.Add(CrewFeature("crew-1", new GeoPoint(-46.00, -23.00)));

        var noGeometryFeature = new GeoFeature { Id = "no-geometry", Geometry = null, Properties = new GeoFeatureProperties { AssetTypeId = "pole" } };
        await orders.AddAsync(DraftOrder("order-no-location", noGeometryFeature));

        var result = await service.GenerateAdvisoryPlanAsync();

        result.SkippedNoLocationOrderIds.Should().ContainSingle().Which.Should().Be("order-no-location");
        result.RankedOrderIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAdvisoryPlanAsync_NoOpenOrders_ReturnsEmptyAdvisoryResult()
    {
        var now = new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);
        var (service, assets, _, _) = await CreateAsync(now);
        assets.Add(CrewFeature("crew-1", new GeoPoint(-46.00, -23.00)));

        var result = await service.GenerateAdvisoryPlanAsync();

        result.SuggestedItineraries.Should().BeEmpty();
        result.RankedOrderIds.Should().BeEmpty();
        result.UnassignedOrderIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateAdvisoryPlanAsync_OrderWithNoBoqAttribute_PredictsZeroValueButStillGetsRouted()
    {
        var now = new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);
        var (service, assets, orders, _) = await CreateAsync(now);

        assets.Add(CrewFeature("crew-1", new GeoPoint(-46.00, -23.00)));
        var pole = PoleFeature("pole-1", new GeoPoint(-46.01, -23.01));
        assets.Add(pole);
        await orders.AddAsync(DraftOrder("order-1", pole));

        var result = await service.GenerateAdvisoryPlanAsync();

        result.RankedOrderIds.Should().Equal("order-1");
        result.UnassignedOrderIds.Should().BeEmpty();
    }
}
