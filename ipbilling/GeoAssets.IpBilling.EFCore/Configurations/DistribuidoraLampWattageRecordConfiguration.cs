using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class DistribuidoraLampWattageRecordConfiguration
    : IEntityTypeConfiguration<DistribuidoraLampWattageRecord>
{
    public void Configure(EntityTypeBuilder<DistribuidoraLampWattageRecord> b)
    {
        b.ToTable("DistribuidoraLampWattages");
        b.HasKey(w => w.Id);

        b.Property(w => w.DistribuidoraId).IsRequired();
        b.Property(w => w.Code).IsRequired().HasConversion<string>().HasMaxLength(8);
        b.Property(w => w.WattageW).IsRequired();
        b.Property(w => w.BallastLossW).IsRequired().HasPrecision(10, 2);

        b.HasIndex(w => new { w.DistribuidoraId, w.Code, w.WattageW }).IsUnique();

        b.HasOne<DistribuidoraRecord>()
         .WithMany()
         .HasForeignKey(w => w.DistribuidoraId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
