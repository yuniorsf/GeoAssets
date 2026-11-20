using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.NeutralOperator.Persistence.Configurations;

internal sealed class OccupancyApplySagaPoleRecordConfiguration : IEntityTypeConfiguration<OccupancyApplySagaPoleRecord>
{
    internal const string TableName = "occupancy_apply_saga_poles";

    public void Configure(EntityTypeBuilder<OccupancyApplySagaPoleRecord> b)
    {
        b.ToTable(TableName);
        b.HasKey(p => new { p.BatchId, p.PoleFeatureId });

        b.Property(p => p.PoleFeatureId).IsRequired().HasMaxLength(PoleOccupancyEntry.MaxFeatureIdLength);
        b.Property(p => p.BaselineVersion).IsRequired();
        b.Property(p => p.AppliedVersion).IsRequired();
        b.Property(p => p.BaselineEntriesJson).IsRequired();
        b.Property(p => p.OrganizationId).IsRequired();

        // A saga's poles live and die with it.
        b.HasOne<OccupancyApplySagaRecord>()
            .WithMany()
            .HasForeignKey(p => p.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        // "Is this pole held by an open saga?"
        b.HasIndex(p => new { p.PoleFeatureId, p.OrganizationId });
    }

    /// <summary>PostgreSQL only: the byte-wise <c>"C"</c> collation of the pole feature ids elsewhere in this schema.</summary>
    internal static void UseOrdinalTextOrder(EntityTypeBuilder<OccupancyApplySagaPoleRecord> b) =>
        b.Property(p => p.PoleFeatureId).UseCollation("C");
}
