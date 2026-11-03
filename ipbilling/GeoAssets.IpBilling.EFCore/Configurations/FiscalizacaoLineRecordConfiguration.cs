using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class FiscalizacaoLineRecordConfiguration : IEntityTypeConfiguration<FiscalizacaoLineRecord>
{
    public void Configure(EntityTypeBuilder<FiscalizacaoLineRecord> b)
    {
        b.ToTable("FiscalizacaoLines");
        b.HasKey(l => l.Id);

        b.Property(l => l.ServiceOrderId).IsRequired().HasMaxLength(64);
        b.Property(l => l.CodigoInstalacao).IsRequired().HasMaxLength(64);
        b.Property(l => l.Agregado).IsRequired();
        b.Property(l => l.Retroativo).IsRequired();

        b.HasIndex(l => new { l.ServiceOrderId, l.CodigoInstalacao }).IsUnique();

        b.HasMany(l => l.OperationGroups)
            .WithOne()
            .HasForeignKey(d => d.LineId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
