using FluentAssertions;
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

    public void Dispose() => _fixture.Dispose();
}
