using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class OrderActionPermissionRecordConfiguration
    : IEntityTypeConfiguration<OrderActionPermissionRecord>
{
    public void Configure(EntityTypeBuilder<OrderActionPermissionRecord> b)
    {
        b.ToTable("OrderActionPermissions");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedOnAdd();

        b.Property(p => p.OrderTypeId).IsRequired().HasMaxLength(128);
        b.Property(p => p.Value).IsRequired().HasMaxLength(256);

        // Index on (OrderTypeId, Action) — powers per-action permission lookup
        b.HasIndex(p => new { p.OrderTypeId, p.Action });
    }
}
