using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// <see cref="GeoAssets.Workflow.Persistence.Entities.ServiceOrderRecord"/> is internal to
/// GeoAssets.Workflow.EFCore, so it can't be named in a <c>modelBuilder.Entity&lt;T&gt;()</c>
/// call from this test project — <c>modelBuilder.Entity(string)</c>'s shadow-state API doesn't
/// need the compile-time type. Replaces EF Core's <see cref="IModelCustomizer"/> (rather than
/// subclassing <see cref="ServiceOrderDbContext"/>, which would need a
/// <c>DbContextOptions&lt;TDerived&gt;</c> constructor incompatible with the base class's own
/// <c>DbContextOptions&lt;ServiceOrderDbContext&gt;</c> constructor) to add a SQLite-only
/// default value for the RowVersion column — SQLite has no native auto-updating
/// rowversion/timestamp column type, unlike the SQL Server target
/// <c>ServiceOrderRecordConfiguration.IsRowVersion()</c> is written for (mirrors
/// <c>SqliteTestDbContext</c> in GeoAssets.Workflow.EFCore.Tests — not reused directly, since
/// this test project has no reference to that one).
/// </summary>
internal sealed class RowVersionDefaultModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);

        modelBuilder.Entity("GeoAssets.Workflow.Persistence.Entities.ServiceOrderRecord")
            .Property("RowVersion")
            .HasDefaultValueSql("randomblob(8)");
    }
}
