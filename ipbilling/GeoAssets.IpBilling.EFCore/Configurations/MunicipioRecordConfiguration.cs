using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class MunicipioRecordConfiguration : IEntityTypeConfiguration<MunicipioRecord>
{
    public void Configure(EntityTypeBuilder<MunicipioRecord> b)
    {
        b.ToTable("Municipios");
        b.HasKey(m => m.Id);

        b.Property(m => m.IbgeCode).IsRequired().HasMaxLength(7);
        b.Property(m => m.Name).IsRequired().HasMaxLength(256);
        b.Property(m => m.Uf).IsRequired().HasMaxLength(2);
        b.Property(m => m.DistribuidoraOrganizationId).IsRequired();

        b.Property(m => m.OrigemLevantamento).IsRequired().HasConversion<string>().HasMaxLength(16);
        b.Property(m => m.OrigemFiscalizacao).IsRequired().HasConversion<string>().HasMaxLength(16);
        b.Property(m => m.OrigemOficio).IsRequired().HasConversion<string>().HasMaxLength(16);
        b.Property(m => m.OrigemMemoriaCalculo).IsRequired().HasConversion<string>().HasMaxLength(16);

        b.HasIndex(m => m.IbgeCode).IsUnique();
        b.HasIndex(m => m.DistribuidoraOrganizationId);
        b.HasIndex(m => m.PrefeituraOrganizationId);
    }
}
