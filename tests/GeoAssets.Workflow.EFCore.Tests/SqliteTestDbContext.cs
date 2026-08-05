using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Test-only subclass of <see cref="ServiceOrderDbContext"/> that layers a SQLite-only
/// default value onto <see cref="ServiceOrderRecord.RowVersion"/>.
///
/// SQLite has no native auto-updating rowversion/timestamp column type — unlike the
/// SQL Server target <c>ServiceOrderRecordConfiguration</c> is written for — so an
/// initial INSERT with no application-supplied value fails a NOT NULL constraint
/// without this. Production model configuration is untouched; <see cref="SqliteFixture"/>
/// additionally installs an AFTER UPDATE trigger (raw SQL, no model changes needed) so
/// the column also changes on every UPDATE, exercising the same optimistic-concurrency
/// path (<c>ServiceOrderConcurrencyException</c>) a real SQL Server host would hit.
/// </summary>
internal sealed class SqliteTestDbContext(DbContextOptions<ServiceOrderDbContext> options)
    : ServiceOrderDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ServiceOrderRecord>()
            .Property(o => o.RowVersion)
            .HasDefaultValueSql("randomblob(8)");
    }
}
