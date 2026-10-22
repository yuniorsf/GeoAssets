using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class LampWattageOptionRecordConfiguration : IEntityTypeConfiguration<LampWattageOptionRecord>
{
    public void Configure(EntityTypeBuilder<LampWattageOptionRecord> b)
    {
        b.ToTable("LampWattageOptions");
        b.HasKey(w => w.Id);

        b.Property(w => w.WattageW).IsRequired();
        b.Property(w => w.BallastLossW).IsRequired().HasPrecision(10, 2);

        b.HasIndex(w => new { w.LampTypeId, w.WattageW }).IsUnique();
    }
}
