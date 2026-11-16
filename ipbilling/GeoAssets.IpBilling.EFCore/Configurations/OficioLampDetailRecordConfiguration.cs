using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class OficioLampDetailRecordConfiguration
    : IEntityTypeConfiguration<OficioLampDetailRecord>
{
    public void Configure(EntityTypeBuilder<OficioLampDetailRecord> b)
    {
        b.ToTable("OficioLampDetails");
        b.HasKey(d => d.Id);

        b.Property(d => d.LineId).IsRequired();
        b.Property(d => d.OperationGroup).IsRequired();
        b.Property(d => d.Code).IsRequired();
        b.Property(d => d.WattageW).IsRequired();
        b.Property(d => d.Quantity).IsRequired();

        b.HasIndex(d => d.LineId);
    }
}
