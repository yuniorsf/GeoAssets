using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class ManholeAuditRecordConfiguration : IEntityTypeConfiguration<ManholeAuditRecord>
{
    public void Configure(EntityTypeBuilder<ManholeAuditRecord> b)
    {
        // Snake_case, matching this ticket's own literal table name — same convention as
        // field_contractor_us_postings/field_icerp_paving_measurements.
        b.ToTable("field_manhole_audits");
        b.HasKey(e => e.Id);

        b.Property(e => e.ConduitFeatureId).IsRequired().HasMaxLength(64);
        b.Property(e => e.OrganizationId).IsRequired();
        b.Property(e => e.SpliceEnclosureCondition).IsRequired().HasMaxLength(256);
        b.Property(e => e.AuditedBy).IsRequired().HasMaxLength(128);
        b.Property(e => e.AuditedAtUtc).IsRequired();

        // Append-only audit history per conduit — no FK to geo_entity (a different database/
        // DbContext, GeoAssets.Provider.PostgreSQL), same reasoning ContractorOrganizationId on
        // ContractorUsPostingRecord already documents for not being a literal FK either.
        b.HasIndex(e => e.ConduitFeatureId);
        b.HasIndex(e => e.OrganizationId);
    }
}
