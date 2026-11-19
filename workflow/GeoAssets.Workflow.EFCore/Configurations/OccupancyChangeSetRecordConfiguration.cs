using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

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
        // cascade, with the order).
        b.HasOne<ServiceOrderTargetRecord>()
         .WithMany()
         .HasForeignKey(s => s.TargetId)
         .OnDelete(DeleteBehavior.Cascade);

        // At most one staged set per target, whatever the application does. Partial: the
        // committed and discarded sets a target accumulates are outside it. It is also what
        // makes two sessions staging for one target exclude each other.
        b.HasIndex(s => s.TargetId, StagedIndexName).IsUnique().HasFilter(StagedFilter);

        // "Every set of this target", for the cascade and for the partial index's blind spot.
        b.HasIndex(s => s.TargetId);

        // "The staged sets of this order."
        b.HasIndex(s => new { s.ServiceOrderId, s.Status });
        b.HasIndex(s => s.OrganizationId);
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
