using FluentAssertions;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Append-only CRUD, FK-to-ServiceOrders, per-contractor/per-period billing-cycle queries, and
/// tenant-isolation coverage for <see cref="EFContractorUsPostingRepository"/> (XD01-242) — same
/// real in-memory SQLite pattern as <see cref="EFUsCatalogRepositoryTests"/>.
/// </summary>
public sealed class EFContractorUsPostingRepositoryTests : IDisposable
{
    private static readonly Guid Cemig = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Cpfl = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly SqliteConnection _connection;

    public EFContractorUsPostingRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var db = NewContext(Cemig);
        db.Database.EnsureCreated();
    }

    private ServiceOrderDbContext NewContext(Guid organizationId)
    {
        var options = new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new ServiceOrderDbContext(options, new FakeTenantContext(organizationId));
    }

    /// <summary>
    /// Inserts a minimal ServiceOrders row via raw SQL rather than
    /// <c>EFServiceOrderRepository.AddAsync</c>, which relies on
    /// <c>ServiceOrderRecord.RowVersion</c> being store-generated (a SQLite-only
    /// default this plain <see cref="ServiceOrderDbContext"/> doesn't install — see
    /// <see cref="SqliteTestDbContext"/>, which this fixture deliberately does not use
    /// since it has no tenant-context constructor). Only exists to satisfy the FK this
    /// ticket's own AC requires on <c>field_contractor_us_postings</c>.
    /// </summary>
    private static async Task<string> SeedServiceOrderAsync(ServiceOrderDbContext db, string id)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO ServiceOrders
                (Id, Title, Description, OrderTypeId, Status, Priority, CreatedBy, OrganizationId, CreatedAt, AttributesJson, FeatureIdsJson, RowVersion)
            VALUES
                ({id}, {"Inspect"}, {""}, {"inspection"}, {ServiceOrderStatus.Draft}, {(int)ServiceOrderPriority.Normal}, {"u1"}, {Guid.Empty}, {new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)}, {"{}"}, {"[]"}, {new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }})
            """);
        return id;
    }

    private static ContractorUsPosting Posting(
        string serviceOrderId,
        Guid contractorOrganizationId,
        decimal quantity = 10m,
        DateTime? postedAtUtc = null) => new()
        {
            ServiceOrderId = serviceOrderId,
            ContractorOrganizationId = contractorOrganizationId,
            ItemCode = "US-EL-014",
            Quantity = quantity,
            UnitUs = 2.5m * 1.2m,
            MonetaryRate = 85.00m,
            PostedAtUtc = postedAtUtc ?? new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc),
        };

    // ── AddAsync / GetByIdAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsAllFields()
    {
        using var db = NewContext(Cemig);
        var orderId = await SeedServiceOrderAsync(db, "order-1");
        var repo = new EFContractorUsPostingRepository(db);
        var posting = Posting(orderId, Cemig);

        await repo.AddAsync(posting);
        await repo.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync(posting.Id);

        loaded.Should().NotBeNull();
        loaded!.ServiceOrderId.Should().Be(orderId);
        loaded.ContractorOrganizationId.Should().Be(Cemig);
        loaded.ItemCode.Should().Be("US-EL-014");
        loaded.Quantity.Should().Be(10m);
        loaded.UnitUs.Should().Be(2.5m * 1.2m);
        loaded.MonetaryRate.Should().Be(85.00m);
        loaded.TotalUs.Should().Be(10m * 2.5m * 1.2m);
        loaded.TotalBrl.Should().Be(10m * 2.5m * 1.2m * 85.00m);
    }

    // ── FK to ServiceOrders ───────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_ReferencingNonexistentServiceOrder_FailsOnSave()
    {
        using var db = NewContext(Cemig);
        var repo = new EFContractorUsPostingRepository(db);
        var posting = Posting("no-such-order", Cemig);

        await repo.AddAsync(posting);
        var act = async () => await repo.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    // ── Append-only (no update/delete path) ──────────────────────────────────

    [Fact]
    public void Repository_HasNoUpdateOrDeleteMethod()
    {
        var methods = typeof(IContractorUsPostingRepository).GetMethods().Select(m => m.Name);

        methods.Should().NotContain(name => name.Contains("Update", StringComparison.Ordinal) || name.Contains("Delete", StringComparison.Ordinal));
    }

    // ── GetByServiceOrderIdAsync ──────────────────────────────────────────────

    [Fact]
    public async Task GetByServiceOrderIdAsync_ReturnsOnlyPostingsForThatOrder_OrderedByPostedAt()
    {
        using var db = NewContext(Cemig);
        var orderA = await SeedServiceOrderAsync(db, "order-a");
        var orderB = await SeedServiceOrderAsync(db, "order-b");
        var repo = new EFContractorUsPostingRepository(db);

        await repo.AddAsync(Posting(orderA, Cemig, postedAtUtc: new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc)));
        await repo.AddAsync(Posting(orderA, Cemig, postedAtUtc: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)));
        await repo.AddAsync(Posting(orderB, Cemig));
        await repo.SaveChangesAsync();

        var results = await repo.GetByServiceOrderIdAsync(orderA);

        results.Should().HaveCount(2);
        results[0].PostedAtUtc.Should().Be(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc));
        results[1].PostedAtUtc.Should().Be(new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc));
    }

    // ── GetByContractorAndPeriodAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetByContractorAndPeriodAsync_OnlyReturnsPostingsWithinTheHalfOpenRange()
    {
        using var db = NewContext(Cemig);
        var orderId = await SeedServiceOrderAsync(db, "order-1");
        var repo = new EFContractorUsPostingRepository(db);

        await repo.AddAsync(Posting(orderId, Cemig, postedAtUtc: new DateTime(2026, 10, 31, 23, 59, 59, DateTimeKind.Utc))); // before window
        await repo.AddAsync(Posting(orderId, Cemig, postedAtUtc: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc))); // start, inclusive
        await repo.AddAsync(Posting(orderId, Cemig, postedAtUtc: new DateTime(2026, 11, 30, 23, 59, 59, DateTimeKind.Utc))); // inside
        await repo.AddAsync(Posting(orderId, Cemig, postedAtUtc: new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc))); // end, exclusive
        await repo.SaveChangesAsync();

        var results = await repo.GetByContractorAndPeriodAsync(
            Cemig, new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));

        results.Should().HaveCount(2);
        results.Should().OnlyContain(p => p.PostedAtUtc >= new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)
            && p.PostedAtUtc < new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetByContractorAndPeriodAsync_DoesNotReturnAnotherContractorsPostings()
    {
        using var dbCemig = NewContext(Cemig);
        var orderCemig = await SeedServiceOrderAsync(dbCemig, "order-cemig");
        await new EFContractorUsPostingRepository(dbCemig).AddAsync(Posting(orderCemig, Cemig));
        await dbCemig.SaveChangesAsync();

        using var dbCpfl = NewContext(Cpfl);
        var orderCpfl = await SeedServiceOrderAsync(dbCpfl, "order-cpfl");
        await new EFContractorUsPostingRepository(dbCpfl).AddAsync(Posting(orderCpfl, Cpfl));
        await dbCpfl.SaveChangesAsync();

        using var queryDb = NewContext(Cemig);
        var results = await new EFContractorUsPostingRepository(queryDb).GetByContractorAndPeriodAsync(
            Cemig, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        results.Should().ContainSingle().Which.ContractorOrganizationId.Should().Be(Cemig);
    }

    // ── Tenant isolation via the ContractorOrganizationId query filter ────────

    [Fact]
    public async Task GetByIdAsync_UnderAnotherContractorsTenantContext_ReturnsNull()
    {
        using var dbCemig = NewContext(Cemig);
        var orderId = await SeedServiceOrderAsync(dbCemig, "order-1");
        var posting = Posting(orderId, Cemig);
        await new EFContractorUsPostingRepository(dbCemig).AddAsync(posting);
        await dbCemig.SaveChangesAsync();

        using var dbCpfl = NewContext(Cpfl);
        var result = await new EFContractorUsPostingRepository(dbCpfl).GetByIdAsync(posting.Id);

        result.Should().BeNull();
    }

    public void Dispose() => _connection.Dispose();
}
