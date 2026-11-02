using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>
/// Test-only subclass of <see cref="ServiceOrderDbContext"/> layering a SQLite-only default
/// value onto <see cref="ServiceOrderRecord.RowVersion"/> — mirrors
/// <c>GeoAssets.Workflow.EFCore.Tests.SqliteTestDbContext</c> (not reusable across assemblies,
/// hence the local copy) for the same reason: SQLite has no native auto-updating rowversion
/// column, unlike the SQL Server target <c>ServiceOrderRecordConfiguration</c> is written for.
/// </summary>
internal sealed class ServiceOrderSqliteTestDbContext(DbContextOptions<ServiceOrderDbContext> options)
    : ServiceOrderDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ServiceOrderRecord>()
            .Property(o => o.RowVersion)
            .HasDefaultValueSql("randomblob(8)");
    }

    /// <summary>
    /// A real SQL Server <c>rowversion</c> column is refreshed in the tracked entity
    /// automatically after every save (EF reads it back via an <c>OUTPUT</c> clause) — this
    /// SQLite trigger-based stand-in has no equivalent, so without an explicit reload here, a
    /// tracked <see cref="ServiceOrderRecord"/> keeps showing the value <em>this context</em>
    /// last wrote even after the AFTER-UPDATE trigger changes it again, causing a second save
    /// on the same already-tracked entity within one logical operation (e.g.
    /// <c>EFServiceOrderRepository.UpdateAsync</c> immediately followed by its own
    /// <c>AppendActionAsync</c>) to spuriously conflict with itself — not a real second writer.
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);

        var trackedEntries = ChangeTracker.Entries<ServiceOrderRecord>()
            .Where(e => e.State == EntityState.Unchanged)
            .ToList();
        foreach (var entry in trackedEntries)
            await entry.ReloadAsync(cancellationToken).ConfigureAwait(false);

        return result;
    }
}
