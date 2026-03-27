using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class OrderDispatchRecordConfiguration : IEntityTypeConfiguration<OrderDispatchRecord>
{
    public void Configure(EntityTypeBuilder<OrderDispatchRecord> b)
    {
        b.ToTable("OrderDispatches");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).ValueGeneratedOnAdd();

        b.Property(d => d.ServiceOrderId).IsRequired().HasMaxLength(36);
        b.Property(d => d.TargetId).IsRequired().HasMaxLength(256);
        b.Property(d => d.DispatchedBy).IsRequired().HasMaxLength(256);
        b.Property(d => d.DispatchedAt).IsRequired();
        b.Property(d => d.Note).HasMaxLength(2048);

        // Index on (TargetId, TargetType) powers GetDispatchedTo queries
        b.HasIndex(d => new { d.TargetId, d.TargetType });
        b.HasIndex(d => d.ServiceOrderId);
    }
}
