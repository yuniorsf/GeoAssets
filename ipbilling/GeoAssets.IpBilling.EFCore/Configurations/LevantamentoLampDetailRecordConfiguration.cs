using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class LevantamentoLampDetailRecordConfiguration : IEntityTypeConfiguration<LevantamentoLampDetailRecord>
{
    public void Configure(EntityTypeBuilder<LevantamentoLampDetailRecord> b)
    {
        b.ToTable("LevantamentoLampDetails");
        b.HasKey(d => d.Id);

        b.Property(d => d.TransformerLineId).IsRequired();
        b.Property(d => d.Code).IsRequired();
        b.Property(d => d.WattageW).IsRequired();
        b.Property(d => d.Quantity).IsRequired();

        b.HasIndex(d => d.TransformerLineId);
    }
}
