using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class Levantamento24hLineRecordConfiguration : IEntityTypeConfiguration<Levantamento24hLineRecord>
{
    public void Configure(EntityTypeBuilder<Levantamento24hLineRecord> b)
    {
        b.ToTable("Levantamento24hLines");
        b.HasKey(l => l.Id);

        b.Property(l => l.ServiceOrderId).IsRequired().HasMaxLength(64);
        b.Property(l => l.FeatureId).IsRequired();
        b.Property(l => l.Code).IsRequired();
        b.Property(l => l.WattageW).IsRequired();
        b.Property(l => l.Quantity).IsRequired();
        b.Property(l => l.Kwh).HasPrecision(18, 4);

        b.HasIndex(l => l.ServiceOrderId);
    }
}
