using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class DistribuidoraRecordConfiguration : IEntityTypeConfiguration<DistribuidoraRecord>
{
    public void Configure(EntityTypeBuilder<DistribuidoraRecord> b)
    {
        b.ToTable("Distribuidoras");
        b.HasKey(d => d.Id);

        b.Property(d => d.OrganizationId).IsRequired();
        b.Property(d => d.Name).IsRequired().HasMaxLength(256);

        b.HasIndex(d => d.OrganizationId);
        b.HasIndex(d => d.Name);
    }
}
