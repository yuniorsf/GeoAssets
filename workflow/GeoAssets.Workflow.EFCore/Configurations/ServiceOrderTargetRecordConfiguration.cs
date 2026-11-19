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

    /// <summary>
    /// XD01-379: a kernel target's <see cref="ServiceOrderTargetRecord.KernelOrderId"/> is its order id; a remote
    /// vertical's is null. Double-quoted identifiers read the same on PostgreSQL and SQLite.
    /// </summary>
    internal const string KernelOrderCheckName = "CK_ServiceOrderTargets_KernelOrderId";

    public void Configure(EntityTypeBuilder<ServiceOrderTargetRecord> b)
    {
        b.ToTable("ServiceOrderTargets", t => t.HasCheckConstraint(
            KernelOrderCheckName,
            $"CASE WHEN \"VerticalId\" = '{Targets.TargetLedger.KernelVerticalId}' " +
            "THEN \"KernelOrderId\" IS NOT NULL AND \"KernelOrderId\" = \"ServiceOrderId\" " +
            "ELSE \"KernelOrderId\" IS NULL END"));
        b.HasKey(t => t.Id);

        b.Property(t => t.Id).HasMaxLength(36);
        b.Property(t => t.ServiceOrderId).IsRequired().HasMaxLength(36);
        // XD01-379: every row written before the global target ledger is an in-process order's.
        b.Property(t => t.VerticalId).IsRequired().HasMaxLength(Targets.TargetLedger.MaxVerticalIdLength)
         .HasDefaultValue(Targets.TargetLedger.KernelVerticalId);
        b.Property(t => t.OrderTypeId).HasMaxLength(64);
        b.Property(t => t.KernelOrderId).HasMaxLength(36);
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

        // XD01-379: the foreign key moved from ServiceOrderId to KernelOrderId, which only an
        // in-process (kernel) target fills — a remote vertical's order has no ServiceOrders row.
        // Deleting (or truncating) an order still deletes its targets.
        b.HasOne<ServiceOrderRecord>()
         .WithMany()
         .HasForeignKey(t => t.KernelOrderId)
         .OnDelete(DeleteBehavior.Cascade);

        // An order — of one vertical — holds a feature at most once; also the conflict target of
        // the repositories' insert-if-absent, and the (vertical, order) lookup of the ledger.
        b.HasIndex(t => new { t.VerticalId, t.ServiceOrderId, t.FeatureId }).IsUnique();

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
