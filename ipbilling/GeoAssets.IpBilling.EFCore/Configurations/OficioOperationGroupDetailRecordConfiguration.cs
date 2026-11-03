using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class OficioOperationGroupDetailRecordConfiguration
    : IEntityTypeConfiguration<OficioOperationGroupDetailRecord>
{
    public void Configure(EntityTypeBuilder<OficioOperationGroupDetailRecord> b)
    {
        b.ToTable("OficioOperationGroupDetails");
        b.HasKey(d => d.Id);

        b.Property(d => d.LineId).IsRequired();
        b.Property(d => d.OperationGroup).IsRequired();
        b.Property(d => d.Count).IsRequired();

        b.HasIndex(d => d.LineId);
    }
}
