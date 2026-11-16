using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class ServiceOrderTargetRecordConfiguration : IEntityTypeConfiguration<ServiceOrderTargetRecord>
{
    /// <summary>
    /// Index predicate for "targets of orders that are still open". Double-quoted identifiers
    /// and <c>IS NULL</c> read the same on PostgreSQL and SQLite, so one literal serves both.
    /// </summary>
    internal const string OpenTargetFilter = "\"ReleasedAt\" IS NULL";

    public void Configure(EntityTypeBuilder<ServiceOrderTargetRecord> b)
    {
        b.ToTable("ServiceOrderTargets");
        b.HasKey(t => t.Id);

        b.Property(t => t.Id).HasMaxLength(36);
        b.Property(t => t.ServiceOrderId).IsRequired().HasMaxLength(36);
        b.Property(t => t.FeatureId).IsRequired().HasMaxLength(64);
        b.Property(t => t.AssetTypeId).IsRequired().HasMaxLength(64);
        b.Property(t => t.Alias).HasMaxLength(256);
        // Unbounded text, same convention as ServiceOrderRecord.AttributesJson.
        b.Property(t => t.ActivityIdsJson).IsRequired();
        b.Property(t => t.Status).IsRequired().HasMaxLength(64);
        b.Property(t => t.ConflictLevel).IsRequired();
        b.Property(t => t.Note).HasMaxLength(2048);
        b.Property(t => t.AddedAt).IsRequired();
        b.Property(t => t.AddedBy).IsRequired().HasMaxLength(256);
        b.Property(t => t.OrganizationId).IsRequired();
        // App-managed (see ServiceOrderTargetRecord.Version): deliberately neither IsRowVersion()
        // nor IsConcurrencyToken(). The repository's update is a single compare-and-swap UPDATE
        // on this column, and stamping ReleasedAt (a different column, written when the order
        // closes) must never fail because a crew edited the target a moment earlier.
        b.Property(t => t.Version).IsRequired().ValueGeneratedNever();

        b.HasOne<ServiceOrderRecord>()
         .WithMany()
         .HasForeignKey(t => t.ServiceOrderId)
         .OnDelete(DeleteBehavior.Cascade);

        // An order holds a feature at most once; also the conflict target of the repository's
        // insert-if-absent.
        b.HasIndex(t => new { t.ServiceOrderId, t.FeatureId }).IsUnique();

        // "Which open orders hold these features?" — partial, so it stays as small as the set of
        // open orders' targets however much closed history accumulates.
        b.HasIndex(t => t.FeatureId).HasFilter(OpenTargetFilter);

        b.HasIndex(t => new { t.ServiceOrderId, t.Status });
        b.HasIndex(t => t.OrganizationId);
    }

    /// <summary>
    /// PostgreSQL only: gives the text columns a target list can be sorted by the byte-wise
    /// <c>"C"</c> collation. The reader contract promises one ordering on every adapter
    /// (ordinal), but a PostgreSQL database's default collation is linguistic (<c>en_US</c>
    /// sorts "a" before "B"; SQLite's BINARY and .NET's ordinal comparison sort "B" first).
    /// </summary>
    internal static void UseOrdinalTextOrder(EntityTypeBuilder<ServiceOrderTargetRecord> b)
    {
        b.Property(t => t.Id).UseCollation("C");
        b.Property(t => t.FeatureId).UseCollation("C");
        b.Property(t => t.Alias).UseCollation("C");
        b.Property(t => t.Status).UseCollation("C");
    }
}
