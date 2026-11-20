using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.NeutralOperator.Persistence.Configurations;

internal sealed class OccupancyChangeSetRecordConfiguration : IEntityTypeConfiguration<OccupancyChangeSetRecord>
{
    internal const string TableName = "ServiceOrderOccupancyChangeSets";

    internal const string StagedIndexName = "IX_ServiceOrderOccupancyChangeSets_TargetId_Staged";

    /// <summary>
    /// Index predicate selecting the staged rows. A double-quoted identifier compared with a
    /// single-quoted literal reads the same on PostgreSQL and SQLite, so one filter serves both.
    /// </summary>
    internal const string StagedFilter = "\"Status\" = 'Staged'";

    public void Configure(EntityTypeBuilder<OccupancyChangeSetRecord> b)
    {
        b.ToTable(TableName);
        b.HasKey(s => s.Id);

        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.ServiceOrderId).IsRequired().HasMaxLength(OccupancyChangeSet.MaxOrderKeyLength);
        b.Property(s => s.TargetId).IsRequired().HasMaxLength(OccupancyChangeSet.MaxOrderKeyLength);
        b.Property(s => s.PoleFeatureId).IsRequired().HasMaxLength(PoleOccupancyEntry.MaxFeatureIdLength);
        // Unbounded text, same convention as ServiceOrderTargetRecord.ActivityIdsJson.
        b.Property(s => s.ChangesJson).IsRequired();
        b.Property(s => s.BaselineVersion).IsRequired();
        b.Property(s => s.Status).IsRequired().HasMaxLength(16);
        b.Property(s => s.StagedBy).IsRequired().HasMaxLength(OccupancyChangeSet.MaxStagedByLength);
        b.Property(s => s.StagedAt).IsRequired();
        b.Property(s => s.OrganizationId).IsRequired();

        // A set lives and dies with the target it was staged on (and, through the target's own
        // cascade, with the order): FK_ServiceOrderOccupancyChangeSets_ServiceOrderTargets_TargetId,
        // ON DELETE CASCADE, to the kernel's "ServiceOrderTargets". Database-only since XD01-380 —
        // the kernel owns that table and this model does not relate to it (KernelTargetRecord is a
        // read-only view, excluded from migrations), so this context's migrations neither create
        // nor drop the constraint; the baseline that adopted the table kept it.

        // At most one staged set per target, whatever the application does. Partial: the
        // committed and discarded sets a target accumulates are outside it. It is also what
        // makes two sessions staging for one target exclude each other.
        b.HasIndex(s => s.TargetId, StagedIndexName).IsUnique().HasFilter(StagedFilter);

        // "Every set of this target", for the cascade and for the partial index's blind spot.
        b.HasIndex(s => s.TargetId);

        // "The staged sets of this order."
        b.HasIndex(s => new { s.ServiceOrderId, s.Status });
        b.HasIndex(s => s.OrganizationId);

        // "The sets of this approval saga" (XD01-383).
        b.HasIndex(s => s.ApplyBatchId);
    }

    /// <summary>
    /// PostgreSQL only: gives the pole feature id the byte-wise <c>"C"</c> collation, for the
    /// same reason as <see cref="PoleOccupancyRecordConfiguration.UseOrdinalTextOrder"/> — the
    /// reader contract orders an order's sets ordinally on every adapter — and the target id
    /// the collation of the column it references.
    /// </summary>
    internal static void UseOrdinalTextOrder(EntityTypeBuilder<OccupancyChangeSetRecord> b)
    {
        b.Property(s => s.PoleFeatureId).UseCollation("C");
        b.Property(s => s.TargetId).UseCollation("C");
    }
}
