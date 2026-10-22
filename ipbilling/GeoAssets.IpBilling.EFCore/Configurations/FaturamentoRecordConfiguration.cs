using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class FaturamentoRecordConfiguration : IEntityTypeConfiguration<FaturamentoRecord>
{
    public void Configure(EntityTypeBuilder<FaturamentoRecord> b)
    {
        b.ToTable("Faturamentos");
        b.HasKey(f => f.Id);

        b.Property(f => f.Competencia).IsRequired().HasMaxLength(7); // "yyyy-MM"
        b.Property(f => f.KwhFaturado).IsRequired().HasPrecision(14, 4);
        b.Property(f => f.CreatedAt).IsRequired();

        b.HasOne<MunicipioRecord>()
         .WithMany()
         .HasForeignKey(f => f.MunicipioId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(f => new { f.MunicipioId, f.Competencia }).IsUnique();
    }
}
