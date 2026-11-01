using FluentAssertions;
using GeoAssets.Workflow.Infrastructure;
using GeoAssets.Workflow.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>Append-only CRUD, per-conduit querying, and tenant-isolation coverage for <see cref="EFManholeAuditRepository"/> (XD01-265) — same real in-memory SQLite pattern as <see cref="EFContractorUsPostingRepositoryTests"/>.</summary>
public sealed class EFManholeAuditRepositoryTests : IDisposable
{
    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly SqliteConnection _connection;

    public EFManholeAuditRepositoryTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var db = NewContext(TenantA);
        db.Database.EnsureCreated();
    }

    private ServiceOrderDbContext NewContext(Guid organizationId)
    {
        var options = new DbContextOptionsBuilder<ServiceOrderDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new ServiceOrderDbContext(options, new FakeTenantContext(organizationId));
    }

    private static ManholeAudit Audit(string conduitFeatureId, Guid organizationId, DateTime? auditedAtUtc = null) => new()
    {
        ConduitFeatureId = conduitFeatureId,
        OrganizationId = organizationId,
        BoreOccupancyPercentage = 62.5,
        SubDucts = [new SubDuct(32, Occupied: true), new SubDuct(40, Occupied: false)],
        SpliceEnclosureCondition = "Good",
        AuditedBy = "tech-1",
        AuditedAtUtc = auditedAtUtc ?? new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc),
    };

    // ── AddAsync / GetByIdAsync ───────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsAllFields()
    {
        using var db = NewContext(TenantA);
        var repo = new EFManholeAuditRepository(db);
        var audit = Audit("conduit-1", TenantA);

        await repo.AddAsync(audit);
        await repo.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync(audit.Id);

        loaded.Should().NotBeNull();
        loaded!.ConduitFeatureId.Should().Be("conduit-1");
        loaded.BoreOccupancyPercentage.Should().Be(62.5);
        loaded.SubDucts.Should().BeEquivalentTo(audit.SubDucts);
        loaded.SpliceEnclosureCondition.Should().Be("Good");
        loaded.AuditedBy.Should().Be("tech-1");
    }

    // ── Append-only (no update/delete path) ──────────────────────────────────

    [Fact]
    public void Repository_HasNoUpdateOrDeleteMethod()
    {
        var methods = typeof(IManholeAuditRepository).GetMethods().Select(m => m.Name);

        methods.Should().NotContain(name => name.Contains("Update", StringComparison.Ordinal) || name.Contains("Delete", StringComparison.Ordinal));
    }

    // ── GetByConduitFeatureIdAsync: "queryable per-conduit" (XD01-265's own AC) ──

    [Fact]
    public async Task GetByConduitFeatureIdAsync_ReturnsOnlyThatConduitsAudits_MostRecentFirst()
    {
        using var db = NewContext(TenantA);
        var repo = new EFManholeAuditRepository(db);

        await repo.AddAsync(Audit("conduit-a", TenantA, auditedAtUtc: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)));
        await repo.AddAsync(Audit("conduit-a", TenantA, auditedAtUtc: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc)));
        await repo.AddAsync(Audit("conduit-b", TenantA));
        await repo.SaveChangesAsync();

        var results = await repo.GetByConduitFeatureIdAsync("conduit-a");

        results.Should().HaveCount(2);
        results[0].AuditedAtUtc.Should().Be(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), "most recent first");
        results[1].AuditedAtUtc.Should().Be(new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    // ── Tenant isolation via the OrganizationId query filter ─────────────────

    [Fact]
    public async Task GetByIdAsync_UnderAnotherTenantsContext_ReturnsNull()
    {
        using var dbA = NewContext(TenantA);
        var audit = Audit("conduit-1", TenantA);
        await new EFManholeAuditRepository(dbA).AddAsync(audit);
        await dbA.SaveChangesAsync();

        using var dbB = NewContext(TenantB);
        var result = await new EFManholeAuditRepository(dbB).GetByIdAsync(audit.Id);

        result.Should().BeNull();
    }

    public void Dispose() => _connection.Dispose();
}
