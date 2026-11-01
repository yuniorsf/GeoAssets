using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class UsCatalogItemRecordConfiguration : IEntityTypeConfiguration<UsCatalogItemRecord>
{
    public void Configure(EntityTypeBuilder<UsCatalogItemRecord> b)
    {
        b.ToTable("UsCatalogItems");
        b.HasKey(e => e.Id);

        b.Property(e => e.OrganizationId).IsRequired().HasDefaultValue(Guid.Empty);
        b.Property(e => e.ItemCode).IsRequired().HasMaxLength(64);
        b.Property(e => e.Description).HasMaxLength(4096);
        b.Property(e => e.Unit).IsRequired().HasMaxLength(32);
        b.Property(e => e.BaseUsFactor).IsRequired().HasPrecision(18, 6);
        b.Property(e => e.RegionalMultiplier).IsRequired().HasPrecision(18, 6).HasDefaultValue(1m);
        b.Property(e => e.MonetaryConversionRate).IsRequired().HasPrecision(18, 6);
        b.Property(e => e.EffectiveFrom).IsRequired();

        // ItemCode is only unique within one concessionaire/contract (OrganizationId) — never
        // globally (XD01-241's own AC: CEMIG's "US-EL-014" and a CPFL item reusing the same code
        // must coexist without collision).
        b.HasIndex(e => new { e.OrganizationId, e.ItemCode });
    }
}
