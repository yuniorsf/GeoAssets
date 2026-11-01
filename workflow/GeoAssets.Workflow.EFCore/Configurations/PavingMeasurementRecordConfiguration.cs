using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class PavingMeasurementRecordConfiguration : IEntityTypeConfiguration<PavingMeasurementRecord>
{
    public void Configure(EntityTypeBuilder<PavingMeasurementRecord> b)
    {
        // Snake_case, matching this ticket's own literal table name — same convention as
        // field_contractor_us_postings.
        b.ToTable("field_icerp_paving_measurements");
        b.HasKey(e => e.Id);

        b.Property(e => e.ParentServiceOrderId).IsRequired().HasMaxLength(36);
        b.Property(e => e.ChildServiceOrderId).IsRequired().HasMaxLength(36);
        b.Property(e => e.OrganizationId).IsRequired();
        b.Property(e => e.RecordedAtUtc).IsRequired();

        // Real FK to ServiceOrders for both the parent and the spawned child, per the same
        // reasoning as ContractorUsPostingRecord's own FK — Restrict, not Cascade: deleting a
        // ServiceOrder must never silently destroy its paving measurement audit history. Two
        // separate FKs need two separate names (EF can't infer distinct ones automatically when
        // both target the same principal table from the same dependent type).
        b.HasOne<ServiceOrderRecord>()
         .WithMany()
         .HasForeignKey(e => e.ParentServiceOrderId)
         .HasConstraintName("FK_field_icerp_paving_measurements_ServiceOrders_Parent")
         .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<ServiceOrderRecord>()
         .WithMany()
         .HasForeignKey(e => e.ChildServiceOrderId)
         .HasConstraintName("FK_field_icerp_paving_measurements_ServiceOrders_Child")
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(e => e.ParentServiceOrderId);
        b.HasIndex(e => e.OrganizationId);
    }
}
