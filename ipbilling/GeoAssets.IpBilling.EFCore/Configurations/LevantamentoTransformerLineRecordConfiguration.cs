using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class LevantamentoTransformerLineRecordConfiguration : IEntityTypeConfiguration<LevantamentoTransformerLineRecord>
{
    public void Configure(EntityTypeBuilder<LevantamentoTransformerLineRecord> b)
    {
        b.ToTable("LevantamentoTransformerLines");
        b.HasKey(l => l.Id);

        b.Property(l => l.ServiceOrderId).IsRequired().HasMaxLength(64);
        b.Property(l => l.CodigoInstalacao).IsRequired().HasMaxLength(64);
        b.Property(l => l.NormalKwh).HasPrecision(18, 4);
        b.Property(l => l.RetroativoKwh).HasPrecision(18, 4);

        b.HasIndex(l => new { l.ServiceOrderId, l.CodigoInstalacao }).IsUnique();

        b.HasMany(l => l.LampDetails)
            .WithOne()
            .HasForeignKey(d => d.TransformerLineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
