using GeoAssets.FieldService.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.FieldService.Persistence.Configurations;

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

        // Real FKs to the kernel's ServiceOrders for both the parent and the spawned child
        // (FK_field_icerp_paving_measurements_ServiceOrders_Parent/_Child), Restrict, not Cascade:
        // deleting a ServiceOrder must never silently destroy its paving measurement audit history.
        // Database-only since XD01-381: this context does not map the kernel's orders, so the
        // constraints are the ones the kernel chain created and the AdoptFieldServiceTables
        // baseline keeps (or creates). The child column keeps the index EF gave the FK.
        b.HasIndex(e => e.ChildServiceOrderId);
        b.HasIndex(e => e.ParentServiceOrderId);
        b.HasIndex(e => e.OrganizationId);
    }
}
