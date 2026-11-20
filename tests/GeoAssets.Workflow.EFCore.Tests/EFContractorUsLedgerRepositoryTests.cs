using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.FieldService.Contractors;
using GeoAssets.FieldService.Persistence;
using GeoAssets.Workflow.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// CRUD and tenant-isolation coverage for <see cref="EFContractorUsLedgerRepository"/> —
/// the EF Core/application-layer half of XD01-202's tenant isolation (the
/// <see cref="ServiceOrderDbContext"/> global query filter). The database-layer half (the
/// Row Level Security policy) is covered separately by
/// <see cref="ContractorUsLedgerEntriesRowLevelSecurityPolicyTests"/>.
///
/// Uses a real in-memory SQLite database (same rationale as <see cref="SqliteFixture"/>)
/// rather than <see cref="SqliteFixture"/> itself, since that fixture always builds its
/// context with the default (<c>NullTenantContext</c>) tenant — these tests need distinct
/// tenants to exercise the filter.
/// </summary>
public sealed class EFContractorUsLedgerRepositoryTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly SqliteConnection _connection;

    public EFContractorUsLedgerRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var db = NewContext(TenantA);
        db.Database.EnsureCreated();
        FieldServiceTestContext.CreateSqliteTables(db);
    }

    private ServiceOrderDbContext NewContext(Guid organizationId)
    {
        var options = new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new ServiceOrderDbContext(options, new FakeTenantContext(organizationId));
    }

    private static ContractorUsLedgerEntry NewEntry(Guid organizationId, string? serviceOrderId = null) => new()
    {
        OrganizationId = organizationId,
        ServiceOrderId = serviceOrderId,
        UsCode = "US-001",
        Description = "Poste trocado",
        Quantity = 2,
        UnitValue = 150.50m,
        PhotoProof =
        [
            new LedgerPhotoProof(
                "https://example.com/p1.jpg",
                new GeoPoint(-46.633, -23.55),
                new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)),
        ],
        CreatedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc),
    };

    // ── AddAsync / GetByIdAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsAllFields()
    {
        using var db = NewContext(TenantA);
        var repo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(db));
        var entry = NewEntry(TenantA, "order-1");

        await repo.AddAsync(entry);
        await repo.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync(entry.Id);

        loaded.Should().NotBeNull();
        loaded!.OrganizationId.Should().Be(TenantA);
        loaded.ServiceOrderId.Should().Be("order-1");
        loaded.UsCode.Should().Be("US-001");
        loaded.Quantity.Should().Be(2);
        loaded.UnitValue.Should().Be(150.50m);
        loaded.TotalValue.Should().Be(301.00m);
        loaded.PhotoProof.Should().ContainSingle();
        loaded.PhotoProof[0].Url.Should().Be("https://example.com/p1.jpg");
        loaded.PhotoProof[0].Location.Longitude.Should().BeApproximately(-46.633, 1e-9);
        loaded.PhotoProof[0].Location.Latitude.Should().BeApproximately(-23.55, 1e-9);
    }

    [Fact]
    public async Task AddAsync_WithoutSaveChanges_DoesNotPersist()
    {
        using var db = NewContext(TenantA);
        var repo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(db));
        var entry = NewEntry(TenantA);

        await repo.AddAsync(entry);
        // Deliberately no SaveChangesAsync() call.

        using var freshDb = NewContext(TenantA);
        var freshRepo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(freshDb));
        (await freshRepo.GetByIdAsync(entry.Id)).Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        using var db = NewContext(TenantA);
        var repo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(db));

        (await repo.GetByIdAsync(Guid.NewGuid())).Should().BeNull();
    }

    // ── Tenant isolation ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_DifferentTenant_ReturnsNull()
    {
        using var writerDb = NewContext(TenantA);
        var entry = NewEntry(TenantA);
        var writerDbRepo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(writerDb));
        await writerDbRepo.AddAsync(entry);
        await writerDbRepo.SaveChangesAsync();

        using var readerDb = NewContext(TenantB);
        var readerRepo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(readerDb));

        (await readerRepo.GetByIdAsync(entry.Id)).Should().BeNull(
            "the global query filter scopes every read to the caller's own tenant, with no cross-contractor exception");
    }

    [Fact]
    public async Task GetAllAsync_OnlyReturnsCallersOwnTenant()
    {
        using (var dbA = NewContext(TenantA))
        {
            var dbARepo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(dbA));
            await dbARepo.AddAsync(NewEntry(TenantA));
            await dbARepo.SaveChangesAsync();
        }
        using (var dbB = NewContext(TenantB))
        {
            var dbBRepo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(dbB));
            await dbBRepo.AddAsync(NewEntry(TenantB));
            await dbBRepo.SaveChangesAsync();
        }

        using var readerDb = NewContext(TenantA);
        var all = await new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(readerDb)).GetAllAsync();

        all.Should().ContainSingle().Which.OrganizationId.Should().Be(TenantA);
    }

    [Fact]
    public async Task GetByServiceOrderIdAsync_FiltersByServiceOrderWithinTenant()
    {
        using var db = NewContext(TenantA);
        var repo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(db));
        await repo.AddAsync(NewEntry(TenantA, "order-1"));
        await repo.AddAsync(NewEntry(TenantA, "order-2"));
        await repo.SaveChangesAsync();

        var results = await repo.GetByServiceOrderIdAsync("order-1");

        results.Should().ContainSingle().Which.ServiceOrderId.Should().Be("order-1");
    }

    [Fact]
    public async Task GetByPeriodAsync_FiltersByCreatedAtWithinTenant()
    {
        using var db = NewContext(TenantA);
        var repo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(db));

        var january = new ContractorUsLedgerEntry
        {
            OrganizationId = TenantA,
            ServiceOrderId = "order-jan",
            UsCode = "US-JAN",
            CreatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
        };
        var february = new ContractorUsLedgerEntry
        {
            OrganizationId = TenantA,
            ServiceOrderId = "order-feb",
            UsCode = "US-FEB",
            CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        };

        await repo.AddAsync(january);
        await repo.AddAsync(february);
        await repo.SaveChangesAsync();

        var results = await repo.GetByPeriodAsync(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));

        results.Should().ContainSingle().Which.ServiceOrderId.Should().Be("order-jan");
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ChangesScalarFields()
    {
        using var db = NewContext(TenantA);
        var repo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(db));
        var entry = NewEntry(TenantA);
        await repo.AddAsync(entry);
        await repo.SaveChangesAsync();

        entry.Description = "Updated description";
        entry.Quantity = 5;
        await repo.UpdateAsync(entry);
        await repo.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync(entry.Id);
        loaded!.Description.Should().Be("Updated description");
        loaded.Quantity.Should().Be(5);
    }

    [Fact]
    public async Task UpdateAsync_DifferentTenant_DoesNotUpdate()
    {
        using var writerDb = NewContext(TenantA);
        var entry = NewEntry(TenantA);
        var writerDbRepo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(writerDb));
        await writerDbRepo.AddAsync(entry);
        await writerDbRepo.SaveChangesAsync();

        using (var attackerDb = NewContext(TenantB))
        {
            entry.Description = "Tampered";
            var attackerDbRepo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(attackerDb));
            await attackerDbRepo.UpdateAsync(entry);
            await attackerDbRepo.SaveChangesAsync();
        }

        using var verifyDb = NewContext(TenantA);
        var loaded = await new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(verifyDb)).GetByIdAsync(entry.Id);
        loaded!.Description.Should().NotBe("Tampered", "a different tenant's update must be a silent no-op, not an actual mutation");
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesEntry()
    {
        using var db = NewContext(TenantA);
        var repo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(db));
        var entry = NewEntry(TenantA);
        await repo.AddAsync(entry);
        await repo.SaveChangesAsync();

        await repo.DeleteAsync(entry.Id);
        await repo.SaveChangesAsync();

        (await repo.GetByIdAsync(entry.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_DifferentTenant_DoesNotDelete()
    {
        using var writerDb = NewContext(TenantA);
        var entry = NewEntry(TenantA);
        var writerDbRepo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(writerDb));
        await writerDbRepo.AddAsync(entry);
        await writerDbRepo.SaveChangesAsync();

        using (var attackerDb = NewContext(TenantB))
        {
            var attackerDbRepo = new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(attackerDb));
            await attackerDbRepo.DeleteAsync(entry.Id);
            await attackerDbRepo.SaveChangesAsync();
        }

        using var verifyDb = NewContext(TenantA);
        (await new EFContractorUsLedgerRepository(FieldServiceTestContext.Over(verifyDb)).GetByIdAsync(entry.Id)).Should().NotBeNull(
            "a different tenant's delete must be a no-op, not silently succeed");
    }

    public void Dispose() => _connection.Dispose();
}
