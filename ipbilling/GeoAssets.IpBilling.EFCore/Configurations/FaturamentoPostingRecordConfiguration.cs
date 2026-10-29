using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class FaturamentoPostingRecordConfiguration
    : IEntityTypeConfiguration<FaturamentoPostingRecord>
{
    public void Configure(EntityTypeBuilder<FaturamentoPostingRecord> b)
    {
        b.ToTable("FaturamentoPostings");
        b.HasKey(p => p.Id);

        b.Property(p => p.ServiceOrderId).IsRequired().HasMaxLength(64);
        b.Property(p => p.Competencia).IsRequired().HasMaxLength(7); // "yyyy-MM"
        b.Property(p => p.Mode).IsRequired();
        b.Property(p => p.DeltaOrBaselineKwh).IsRequired().HasPrecision(14, 4);
        b.Property(p => p.ResultingKwh).IsRequired().HasPrecision(14, 4);
        b.Property(p => p.PostedAt).IsRequired();

        b.HasOne<MunicipioRecord>()
         .WithMany()
         .HasForeignKey(p => p.MunicipioId)
         .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(p => new { p.ServiceOrderId, p.Competencia }).IsUnique();
    }
}
