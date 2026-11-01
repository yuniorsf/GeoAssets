using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Outbox;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// XD01-228's AC: a single ACID transaction advancing <see cref="ServiceOrder.Status"/> to
/// <see cref="FieldWorkStatus.Inspected"/>, posting <see cref="ContractorUsPosting"/> ledger
/// rows, and staging the outbox event — all three commit or roll back together, and a stale
/// <c>RowVersion</c> is rejected rather than silently double-crediting.
/// </summary>
public sealed class InspectionCommitServiceTests : IDisposable
{
    private static readonly DateTimeOffset FixedNowUtc = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteFixture _fixture = new();
    private readonly OrderTypeRegistry _registry = new();
    private readonly FakeTimeProvider _timeProvider = new(FixedNowUtc);

    public InspectionCommitServiceTests() => FieldWorkOrderTypes.RegisterAll(_registry);

    private InspectionCommitService BuildService(string sourceService = "test-service") => new(
        _fixture.Context,
        new ValidatingServiceOrderRepository(new EFServiceOrderRepository(_fixture.Context, orderTypeRegistry: _registry), _registry),
        new EFContractorUsPostingRepository(_fixture.Context),
        new EFUsCatalogRepository(_fixture.Context),
        new EFPavingMeasurementRepository(_fixture.Context),
        new EFOutboxStore(_fixture.Context, _timeProvider, new ProtobufIntegrationMessageSerializer(), sourceService),
        _registry,
        _timeProvider);

    private async Task<(string OrderId, byte[] RowVersion)> SeedOrderAsync(string boqLinesJson)
    {
        var repo = new EFServiceOrderRepository(_fixture.Context, orderTypeRegistry: _registry);
        var order = new ServiceOrder
        {
            OrderTypeId = FieldWorkOrderTypes.FieldWorkId,
            Status = FieldWorkStatus.Completed,
            CreatedBy = "tech-1",
            OrganizationId = Guid.Empty,
            CreatedAt = FixedNowUtc.UtcDateTime,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ServiceOrderBoqAttributes.ContractorOrganizationIdKey] = Guid.Empty.ToString(),
                [ServiceOrderBoqAttributes.BoqLinesKey] = boqLinesJson,
            },
        };

        await repo.AddAsync(order);
        var loaded = await repo.GetByIdAsync(order.Id) ?? throw new InvalidOperationException("Seed order not found.");
        return (loaded.Id, loaded.RowVersion);
    }

    private async Task SeedCatalogItemAsync(string itemCode, decimal baseUsFactor, decimal regionalMultiplier, decimal monetaryConversionRate)
    {
        var repo = new EFUsCatalogRepository(_fixture.Context);
        await repo.AddAsync(new UsCatalogItem
        {
            OrganizationId = Guid.Empty,
            ItemCode = itemCode,
            BaseUsFactor = baseUsFactor,
            RegionalMultiplier = regionalMultiplier,
            MonetaryConversionRate = monetaryConversionRate,
            EffectiveFrom = FixedNowUtc.UtcDateTime.AddDays(-1),
        });
        await repo.SaveChangesAsync();
    }

    // ── Happy path: all three writes commit atomically ──────────────────────────

    [Fact]
    public async Task CommitAsync_HappyPath_AdvancesStatus_PostsLedger_AndStagesOutboxPending()
    {
        await SeedCatalogItemAsync("US-EL-014", baseUsFactor: 2.5m, regionalMultiplier: 1.2m, monetaryConversionRate: 85m);
        var (orderId, rowVersion) = await SeedOrderAsync("""[{"itemCode":"US-EL-014","quantity":2}]""");
        var service = BuildService();

        var result = await service.CommitAsync(orderId, rowVersion, performedByUserId: "inspector-1", catalogInstanceId: "catalog-primary");

        result.TotalUs.Should().Be(2m * 2.5m * 1.2m);
        result.TotalBrl.Should().Be(2m * 2.5m * 1.2m * 85m);
        result.Postings.Should().HaveCount(1);

        // A fresh DbContext (not _fixture.Context) for every post-commit read below: EF Core's
        // identity map would otherwise hand back the SAME tracked, possibly stale instance that
        // InspectionCommitService's own repositories mutated in memory, masking what's actually
        // committed to the database (see the rollback test's own note on this same pitfall).
        using var verifyContext = _fixture.NewContext();
        var reloadedOrder = await new EFServiceOrderRepository(verifyContext, orderTypeRegistry: _registry).GetByIdAsync(orderId);
        reloadedOrder!.Status.Should().Be(FieldWorkStatus.Inspected);
        reloadedOrder.RowVersion.Should().NotBeEquivalentTo(rowVersion, "the update must have advanced RowVersion");

        var postedLedgerRows = await new EFContractorUsPostingRepository(verifyContext).GetByServiceOrderIdAsync(orderId);
        postedLedgerRows.Should().ContainSingle().Which.TotalUs.Should().Be(result.TotalUs);

        var outbox = new EFOutboxStore(verifyContext, _timeProvider, new ProtobufIntegrationMessageSerializer(), "verify-worker");
        var claimed = await outbox.ClaimPendingBatchAsync(batchSize: 10, leaseDuration: TimeSpan.FromMinutes(5), workerId: "verify-worker");
        claimed.Should().ContainSingle(m => m.Envelope.PartitionKey == orderId, "the staged message was PENDING immediately before this claim");
    }

    // ── Optimistic concurrency: stale RowVersion is rejected, nothing partially committed ──────

    [Fact]
    public async Task CommitAsync_StaleRowVersion_ThrowsAndLeavesNoPartialState()
    {
        await SeedCatalogItemAsync("US-EL-014", baseUsFactor: 2.5m, regionalMultiplier: 1.2m, monetaryConversionRate: 85m);
        var (orderId, _) = await SeedOrderAsync("""[{"itemCode":"US-EL-014","quantity":2}]""");
        var staleRowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var service = BuildService();

        var act = async () => await service.CommitAsync(orderId, staleRowVersion, performedByUserId: "inspector-1", catalogInstanceId: "catalog-primary");

        await act.Should().ThrowAsync<ServiceOrderConcurrencyException>();

        // Fresh DbContext: _fixture.Context's own change tracker still holds the in-memory
        // mutation EFServiceOrderRepository.UpdateAsync applied to its tracked entity before
        // SaveChangesAsync failed — EF does not revert that on a failed save, so re-querying
        // through the SAME context would show the pre-rollback mutation, not the true (rolled
        // back) database state.
        using var verifyContext = _fixture.NewContext();
        var reloadedOrder = await new EFServiceOrderRepository(verifyContext, orderTypeRegistry: _registry).GetByIdAsync(orderId);
        reloadedOrder!.Status.Should().Be(FieldWorkStatus.Completed, "the crashed transaction must leave the order's status unchanged");

        var postedLedgerRows = await new EFContractorUsPostingRepository(verifyContext).GetByServiceOrderIdAsync(orderId);
        postedLedgerRows.Should().BeEmpty("the crashed transaction must leave zero ledger rows, even though the posting was already flushed before the rollback");
    }

    // ── Missing catalog item mid-transaction: same all-or-nothing guarantee ────────────────────

    [Fact]
    public async Task CommitAsync_MissingCatalogItem_ThrowsAndLeavesNoPartialState()
    {
        await SeedCatalogItemAsync("US-EL-014", baseUsFactor: 2.5m, regionalMultiplier: 1.2m, monetaryConversionRate: 85m);
        var (orderId, rowVersion) = await SeedOrderAsync(
            """[{"itemCode":"US-EL-014","quantity":2},{"itemCode":"NO-SUCH-ITEM","quantity":1}]""");
        var service = BuildService();

        var act = async () => await service.CommitAsync(orderId, rowVersion, performedByUserId: "inspector-1", catalogInstanceId: "catalog-primary");

        await act.Should().ThrowAsync<UsCatalogItemNotFoundException>();

        using var verifyContext = _fixture.NewContext();
        var reloadedOrder = await new EFServiceOrderRepository(verifyContext, orderTypeRegistry: _registry).GetByIdAsync(orderId);
        reloadedOrder!.Status.Should().Be(FieldWorkStatus.Completed);

        var postedLedgerRows = await new EFContractorUsPostingRepository(verifyContext).GetByServiceOrderIdAsync(orderId);
        postedLedgerRows.Should().BeEmpty("the first line's posting must roll back along with the second line's failure");
    }

    // ── No BoQ lines: still a valid (zero-ledger) inspection commit ────────────────────────────

    [Fact]
    public async Task CommitAsync_NoBoqLines_StillTransitionsAndStagesOutboxWithZeroLedgerRows()
    {
        var (orderId, rowVersion) = await SeedOrderAsync("[]");
        var service = BuildService();

        var result = await service.CommitAsync(orderId, rowVersion, performedByUserId: "inspector-1", catalogInstanceId: "catalog-primary");

        result.Postings.Should().BeEmpty();
        result.TotalUs.Should().Be(0);

        using var verifyContext = _fixture.NewContext();
        var reloadedOrder = await new EFServiceOrderRepository(verifyContext, orderTypeRegistry: _registry).GetByIdAsync(orderId);
        reloadedOrder!.Status.Should().Be(FieldWorkStatus.Inspected);
    }

    // ── Paving sub-order spawn: parent transition + ledger + outbox + child order + measurement ──

    private const double MetersPerDegreeLatitude = 111_320.0; // matches GeodeticAreaCalculatorTests' own constant

    private static readonly OrderType WaterSewerRepairOrderType = FieldWorkWorkflowGraph.BuildOrderType("water-sewer-repair", "Water/Sewer Repair");
    private static readonly OrderType PavingRestorationOrderType = FieldWorkWorkflowGraph.BuildOrderType("water-paving-restoration", "Water Paving Restoration");

    private static GeoPolygon SquareOfSide(double originLon, double originLat, double sideMeters)
    {
        var metersPerDegreeLongitude = MetersPerDegreeLatitude * Math.Cos(originLat * Math.PI / 180.0);
        var deltaLon = sideMeters / metersPerDegreeLongitude;
        var deltaLat = sideMeters / MetersPerDegreeLatitude;

        return new GeoPolygon([
            (originLon, originLat),
            (originLon + deltaLon, originLat),
            (originLon + deltaLon, originLat + deltaLat),
            (originLon, originLat + deltaLat),
            (originLon, originLat),
        ]);
    }

    private async Task<(string OrderId, byte[] RowVersion)> SeedPavingEligibleOrderAsync(
        double trenchLengthMeters, double trenchWidthMeters, string boqLinesJson = "[]", string? scopeId = null)
    {
        _registry.Register(WaterSewerRepairOrderType);
        _registry.Register(PavingRestorationOrderType);

        var boundary = SquareOfSide(-46.633, -23.55, sideMeters: 10); // actual area = 100 m²
        var boundaryGeoJson = JsonSerializer.Serialize(boundary, GeoJsonSerializer.Options);

        var repo = new EFServiceOrderRepository(_fixture.Context, orderTypeRegistry: _registry);
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ServiceOrderBoqAttributes.ContractorOrganizationIdKey] = Guid.Empty.ToString(),
            [ServiceOrderBoqAttributes.BoqLinesKey] = boqLinesJson,
            [ServiceOrderPavingAttributes.PavingEligibleKey] = "true",
            [ServiceOrderPavingAttributes.PavingOrderTypeIdKey] = PavingRestorationOrderType.Id,
            [ServiceOrderPavingAttributes.RecordedTrenchLengthMetersKey] = trenchLengthMeters.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [ServiceOrderPavingAttributes.RecordedTrenchWidthMetersKey] = trenchWidthMeters.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [ServiceOrderPavingAttributes.RecordedTrenchDepthMetersKey] = "1.5",
            [ServiceOrderPavingAttributes.RecordedBoundaryGeoJsonKey] = boundaryGeoJson,
        };
        if (scopeId is not null)
            attributes["scopeId"] = scopeId;

        var order = new ServiceOrder
        {
            OrderTypeId = WaterSewerRepairOrderType.Id,
            Status = FieldWorkStatus.Completed,
            CreatedBy = "tech-1",
            OrganizationId = Guid.Empty,
            CreatedAt = FixedNowUtc.UtcDateTime,
            Attributes = attributes,
        };

        await repo.AddAsync(order);
        var loaded = await repo.GetByIdAsync(order.Id) ?? throw new InvalidOperationException("Seed order not found.");
        return (loaded.Id, loaded.RowVersion);
    }

    [Fact]
    public async Task CommitAsync_PavingEligible_GeometryWithinTolerance_SpawnsChildAndPersistsMeasurementAtomically()
    {
        var (orderId, rowVersion) = await SeedPavingEligibleOrderAsync(trenchLengthMeters: 10, trenchWidthMeters: 10); // area = 100 m², no divergence
        var service = BuildService();

        var result = await service.CommitAsync(orderId, rowVersion, performedByUserId: "inspector-1", catalogInstanceId: "catalog-primary");

        result.PavingChildServiceOrderId.Should().NotBeNullOrEmpty();

        using var verifyContext = _fixture.NewContext();
        var reloadedParent = await new EFServiceOrderRepository(verifyContext, orderTypeRegistry: _registry).GetByIdAsync(orderId);
        reloadedParent!.Status.Should().Be(FieldWorkStatus.Inspected);
        reloadedParent.ChildOrderIds.Should().Contain(result.PavingChildServiceOrderId,
            "the parent/child relationship must be visible from the parent's own audit trail too (XD01-252)");

        var reloadedChild = await new EFServiceOrderRepository(verifyContext, orderTypeRegistry: _registry).GetByIdAsync(result.PavingChildServiceOrderId!);
        reloadedChild.Should().NotBeNull();
        reloadedChild!.ParentOrderId.Should().Be(orderId);
        reloadedChild.OrderTypeId.Should().Be(PavingRestorationOrderType.Id);

        var measurements = await new EFPavingMeasurementRepository(verifyContext).GetByParentServiceOrderIdAsync(orderId);
        measurements.Should().ContainSingle();
        measurements[0].ChildServiceOrderId.Should().Be(result.PavingChildServiceOrderId);
        measurements[0].RecordedAreaSquareMeters.Should().Be(100.0);
        measurements[0].VolumeCubicMeters.Should().Be(150.0);
    }

    [Fact]
    public async Task CommitAsync_PavingEligible_ParentHasScopeId_PersistedChildInheritsIt()
    {
        var (orderId, rowVersion) = await SeedPavingEligibleOrderAsync(trenchLengthMeters: 10, trenchWidthMeters: 10, scopeId: "municipio-123");
        var service = BuildService();

        var result = await service.CommitAsync(orderId, rowVersion, performedByUserId: "inspector-1", catalogInstanceId: "catalog-primary");

        using var verifyContext = _fixture.NewContext();
        var reloadedChild = await new EFServiceOrderRepository(verifyContext, orderTypeRegistry: _registry).GetByIdAsync(result.PavingChildServiceOrderId!);

        reloadedChild!.Attributes["scopeId"].Should().Be("municipio-123",
            "a grant scoped to the parent's município must also authorize the persisted child after reload, not just the in-memory spawn (XD01-252)");
    }

    [Fact]
    public async Task CommitAsync_PavingEligible_GeometryDivergesBeyondTolerance_RollsBackEveryWrite()
    {
        await SeedCatalogItemAsync("US-EL-014", baseUsFactor: 2.5m, regionalMultiplier: 1.2m, monetaryConversionRate: 85m);
        var (orderId, rowVersion) = await SeedPavingEligibleOrderAsync(
            trenchLengthMeters: 20, trenchWidthMeters: 10, // area = 200 m² vs. actual 100 m² -> way beyond 15%
            boqLinesJson: """[{"itemCode":"US-EL-014","quantity":2}]""");
        var service = BuildService();

        var act = async () => await service.CommitAsync(orderId, rowVersion, performedByUserId: "inspector-1", catalogInstanceId: "catalog-primary");

        await act.Should().ThrowAsync<TrenchGeometryDivergenceException>();

        using var verifyContext = _fixture.NewContext();
        var reloadedParent = await new EFServiceOrderRepository(verifyContext, orderTypeRegistry: _registry).GetByIdAsync(orderId);
        reloadedParent!.Status.Should().Be(FieldWorkStatus.Completed, "a rejected geometry check must roll back the parent transition along with every other write");

        var postedLedgerRows = await new EFContractorUsPostingRepository(verifyContext).GetByServiceOrderIdAsync(orderId);
        postedLedgerRows.Should().BeEmpty("the ledger line posted before the geometry check must roll back too");

        var measurements = await new EFPavingMeasurementRepository(verifyContext).GetByParentServiceOrderIdAsync(orderId);
        measurements.Should().BeEmpty("no measurement row is ever written when the geometry check itself rejects the commit");
    }

    public void Dispose() => _fixture.Dispose();
}
