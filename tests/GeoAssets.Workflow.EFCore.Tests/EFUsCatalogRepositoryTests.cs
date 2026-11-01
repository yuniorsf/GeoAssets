using FluentAssertions;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// CRUD, effective-dated versioning, and tenant-isolation coverage for
/// <see cref="EFUsCatalogRepository"/> (XD01-241) — same real in-memory SQLite pattern as
/// <see cref="EFContractorUsLedgerRepositoryTests"/>.
/// </summary>
public sealed class EFUsCatalogRepositoryTests : IDisposable
{
    private static readonly Guid Cemig = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Cpfl = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly SqliteConnection _connection;

    public EFUsCatalogRepositoryTests()
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

    private static UsCatalogItem NewItem(Guid organizationId, DateTime effectiveFrom, DateTime? effectiveTo = null) => new()
    {
        OrganizationId = organizationId,
        ItemCode = "US-EL-014",
        Description = "Troca de poste",
        Unit = "un",
        BaseUsFactor = 2.5m,
        RegionalMultiplier = 1.2m,
        MonetaryConversionRate = 85.00m,
        EffectiveFrom = effectiveFrom,
        EffectiveTo = effectiveTo,
    };

    // ── AddAsync / GetByIdAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsAllFields()
    {
        using var db = NewContext(Cemig);
        var repo = new EFUsCatalogRepository(db);
        var item = NewItem(Cemig, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        await repo.AddAsync(item);
        await repo.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync(item.Id);

        loaded.Should().NotBeNull();
        loaded!.OrganizationId.Should().Be(Cemig);
        loaded.ItemCode.Should().Be("US-EL-014");
        loaded.BaseUsFactor.Should().Be(2.5m);
        loaded.RegionalMultiplier.Should().Be(1.2m);
        loaded.MonetaryConversionRate.Should().Be(85.00m);
        loaded.EffectiveTo.Should().BeNull();
    }

    // ── Per-concessionaire coexistence (no collision) ───────────────────────────

    [Fact]
    public async Task SameItemCode_UnderTwoDifferentOrganizations_CoexistsWithoutCollision()
    {
        using var dbCemig = NewContext(Cemig);
        await new EFUsCatalogRepository(dbCemig).AddAsync(NewItem(Cemig, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await dbCemig.SaveChangesAsync();

        using var dbCpfl = NewContext(Cpfl);
        var cpflItem = NewItem(Cpfl, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        cpflItem.BaseUsFactor = 9.9m; // a deliberately different factor for the "same" code
        await new EFUsCatalogRepository(dbCpfl).AddAsync(cpflItem);
        await dbCpfl.SaveChangesAsync();

        using var queryDb = NewContext(Cemig);
        var cemigResult = await new EFUsCatalogRepository(queryDb).FindEffectiveAsync(Cemig, "US-EL-014", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        using var queryDb2 = NewContext(Cpfl);
        var cpflResult = await new EFUsCatalogRepository(queryDb2).FindEffectiveAsync(Cpfl, "US-EL-014", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        cemigResult!.BaseUsFactor.Should().Be(2.5m);
        cpflResult!.BaseUsFactor.Should().Be(9.9m);
    }

    // ── Effective-dated versioning ───────────────────────────────────────────────

    [Fact]
    public async Task FindEffectiveAsync_MultipleVersions_ReturnsTheOneEffectiveAtTheGivenInstant()
    {
        using var db = NewContext(Cemig);
        var repo = new EFUsCatalogRepository(db);

        var oldVersion = NewItem(Cemig, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        oldVersion.BaseUsFactor = 2.0m;
        var newVersion = NewItem(Cemig, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        newVersion.BaseUsFactor = 2.5m;

        await repo.AddAsync(oldVersion);
        await repo.AddAsync(newVersion);
        await repo.SaveChangesAsync();

        var beforeCutover = await repo.FindEffectiveAsync(Cemig, "US-EL-014", new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var afterCutover = await repo.FindEffectiveAsync(Cemig, "US-EL-014", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        beforeCutover!.BaseUsFactor.Should().Be(2.0m);
        afterCutover!.BaseUsFactor.Should().Be(2.5m);
    }

    [Fact]
    public async Task FindEffectiveAsync_BeforeAnyVersionExisted_ReturnsNull()
    {
        using var db = NewContext(Cemig);
        var repo = new EFUsCatalogRepository(db);
        await repo.AddAsync(NewItem(Cemig, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await repo.SaveChangesAsync();

        var result = await repo.FindEffectiveAsync(Cemig, "US-EL-014", new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        result.Should().BeNull();
    }

    [Fact]
    public async Task FindEffectiveAsync_ExactlyAtEffectiveFrom_IsIncluded()
    {
        using var db = NewContext(Cemig);
        var repo = new EFUsCatalogRepository(db);
        var effectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await repo.AddAsync(NewItem(Cemig, effectiveFrom));
        await repo.SaveChangesAsync();

        var result = await repo.FindEffectiveAsync(Cemig, "US-EL-014", effectiveFrom);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task FindEffectiveAsync_ExactlyAtEffectiveTo_IsExcluded()
    {
        using var db = NewContext(Cemig);
        var repo = new EFUsCatalogRepository(db);
        var effectiveTo = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await repo.AddAsync(NewItem(Cemig, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc), effectiveTo));
        await repo.SaveChangesAsync();

        var result = await repo.FindEffectiveAsync(Cemig, "US-EL-014", effectiveTo);

        result.Should().BeNull("EffectiveTo is an exclusive upper bound — the next version takes over exactly then");
    }

    // ── GetByOrganizationAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetByOrganizationAsync_OnlyReturnsThatOrganizationsItems()
    {
        using var dbCemig = NewContext(Cemig);
        await new EFUsCatalogRepository(dbCemig).AddAsync(NewItem(Cemig, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await dbCemig.SaveChangesAsync();

        using var dbCpfl = NewContext(Cpfl);
        await new EFUsCatalogRepository(dbCpfl).AddAsync(NewItem(Cpfl, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await dbCpfl.SaveChangesAsync();

        using var queryDb = NewContext(Cemig);
        var results = await new EFUsCatalogRepository(queryDb).GetByOrganizationAsync(Cemig);

        results.Should().ContainSingle().Which.OrganizationId.Should().Be(Cemig);
    }

    // ── UpdateAsync / DeleteAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ChangesPersistedFields()
    {
        using var db = NewContext(Cemig);
        var repo = new EFUsCatalogRepository(db);
        var item = NewItem(Cemig, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await repo.AddAsync(item);
        await repo.SaveChangesAsync();

        item.BaseUsFactor = 3.3m;
        await repo.UpdateAsync(item);
        await repo.SaveChangesAsync();

        var reloaded = await repo.GetByIdAsync(item.Id);
        reloaded!.BaseUsFactor.Should().Be(3.3m);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheItem()
    {
        using var db = NewContext(Cemig);
        var repo = new EFUsCatalogRepository(db);
        var item = NewItem(Cemig, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        await repo.AddAsync(item);
        await repo.SaveChangesAsync();

        await repo.DeleteAsync(item.Id);
        await repo.SaveChangesAsync();

        (await repo.GetByIdAsync(item.Id)).Should().BeNull();
    }

    public void Dispose() => _connection.Dispose();
}
