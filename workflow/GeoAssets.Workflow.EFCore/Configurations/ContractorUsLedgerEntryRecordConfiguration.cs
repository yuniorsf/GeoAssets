using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class ContractorUsLedgerEntryRecordConfiguration : IEntityTypeConfiguration<ContractorUsLedgerEntryRecord>
{
    public void Configure(EntityTypeBuilder<ContractorUsLedgerEntryRecord> b)
    {
        b.ToTable("ContractorUsLedgerEntries");
        b.HasKey(e => e.Id);

        b.Property(e => e.OrganizationId).IsRequired().HasDefaultValue(Guid.Empty);
        b.Property(e => e.ServiceOrderId).HasMaxLength(36);
        b.Property(e => e.UsCode).IsRequired().HasMaxLength(64);
        b.Property(e => e.Description).HasMaxLength(4096);
        b.Property(e => e.Quantity).IsRequired().HasPrecision(18, 4);
        b.Property(e => e.UnitValue).IsRequired().HasPrecision(18, 4);
        b.Property(e => e.PhotoProofJson).IsRequired();
        b.Property(e => e.CreatedAt).IsRequired();

        b.HasIndex(e => e.OrganizationId);
        b.HasIndex(e => e.ServiceOrderId);
    }
}
