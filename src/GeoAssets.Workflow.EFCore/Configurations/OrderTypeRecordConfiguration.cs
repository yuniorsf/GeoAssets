using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class OrderTypeRecordConfiguration : IEntityTypeConfiguration<OrderTypeRecord>
{
    public void Configure(EntityTypeBuilder<OrderTypeRecord> b)
    {
        b.ToTable("OrderTypes");
        b.HasKey(t => t.Id);

        b.Property(t => t.Id).HasMaxLength(128);
        b.Property(t => t.DisplayName).IsRequired().HasMaxLength(256);
        b.Property(t => t.Description).HasMaxLength(1024);

        b.HasMany(t => t.CreationPolicies)
         .WithOne(p => p.OrderType)
         .HasForeignKey(p => p.OrderTypeId)
         .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(t => t.ActionPermissions)
         .WithOne(p => p.OrderType)
         .HasForeignKey(p => p.OrderTypeId)
         .OnDelete(DeleteBehavior.Cascade);
    }
}
