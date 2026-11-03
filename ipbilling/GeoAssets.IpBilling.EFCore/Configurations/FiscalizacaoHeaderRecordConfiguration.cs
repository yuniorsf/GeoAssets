using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class FiscalizacaoHeaderRecordConfiguration : IEntityTypeConfiguration<FiscalizacaoHeaderRecord>
{
    public void Configure(EntityTypeBuilder<FiscalizacaoHeaderRecord> b)
    {
        b.ToTable("FiscalizacaoHeaders");
        b.HasKey(h => h.ServiceOrderId);

        b.Property(h => h.ServiceOrderId).IsRequired().HasMaxLength(64);
        b.Property(h => h.Competencia).IsRequired().HasMaxLength(7);
        b.Property(h => h.MunicipioId).IsRequired();
        b.Property(h => h.IsValidated).IsRequired();

        b.HasIndex(h => new { h.MunicipioId, h.Competencia });
    }
}
