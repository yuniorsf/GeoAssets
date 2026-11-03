using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class OficioHeaderRecordConfiguration : IEntityTypeConfiguration<OficioHeaderRecord>
{
    public void Configure(EntityTypeBuilder<OficioHeaderRecord> b)
    {
        b.ToTable("OficioHeaders");
        b.HasKey(h => h.Id);

        b.Property(h => h.ServiceOrderId).IsRequired().HasMaxLength(64);
        b.Property(h => h.Competencia).IsRequired().HasMaxLength(7);
        b.Property(h => h.MunicipioId).IsRequired();
        b.Property(h => h.NumeroPrefeitura).HasMaxLength(64);
        b.Property(h => h.IsValidated).IsRequired();

        b.HasIndex(h => h.ServiceOrderId).IsUnique();
        b.HasIndex(h => new { h.MunicipioId, h.Competencia });

        // Duplicate-upload guard (XD01-185): a given Prefeitura-assigned número can only be
        // claimed by one header per município at a time. Partial so multiple headers with no
        // número yet (NULL) never collide, and so OficioCalculationService.SupersedeAsync can
        // free a número for reassignment by clearing it back to NULL.
        b.HasIndex(h => new { h.MunicipioId, h.NumeroPrefeitura })
            .IsUnique()
            .HasFilter("\"NumeroPrefeitura\" IS NOT NULL");
    }
}
