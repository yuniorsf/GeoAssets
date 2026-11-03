using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class FiscalizacaoOperationGroupDetailRecordConfiguration
    : IEntityTypeConfiguration<FiscalizacaoOperationGroupDetailRecord>
{
    public void Configure(EntityTypeBuilder<FiscalizacaoOperationGroupDetailRecord> b)
    {
        b.ToTable("FiscalizacaoOperationGroupDetails");
        b.HasKey(d => d.Id);

        b.Property(d => d.LineId).IsRequired();
        b.Property(d => d.OperationGroup).IsRequired();
        b.Property(d => d.Count).IsRequired();

        b.HasIndex(d => d.LineId);
    }
}
