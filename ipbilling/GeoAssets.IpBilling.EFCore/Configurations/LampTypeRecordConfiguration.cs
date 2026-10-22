using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.IpBilling.Persistence.Configurations;

internal sealed class LampTypeRecordConfiguration : IEntityTypeConfiguration<LampTypeRecord>
{
    public void Configure(EntityTypeBuilder<LampTypeRecord> b)
    {
        b.ToTable("LampTypes");
        b.HasKey(t => t.Id);

        b.Property(t => t.Code).IsRequired().HasConversion<string>().HasMaxLength(8);
        b.Property(t => t.Name).IsRequired().HasMaxLength(128);

        b.HasIndex(t => t.Code).IsUnique();

        b.HasMany(t => t.WattageOptions)
         .WithOne(w => w.LampType)
         .HasForeignKey(w => w.LampTypeId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
