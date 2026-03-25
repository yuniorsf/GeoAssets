using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class OrderActionLogRecordConfiguration : IEntityTypeConfiguration<OrderActionLogRecord>
{
    public void Configure(EntityTypeBuilder<OrderActionLogRecord> b)
    {
        b.ToTable("OrderActionLogs");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedOnAdd();

        b.Property(a => a.ServiceOrderId).IsRequired().HasMaxLength(36);
        b.Property(a => a.PerformedBy).IsRequired().HasMaxLength(256);
        b.Property(a => a.PerformedAt).IsRequired();
        b.Property(a => a.Comment).HasMaxLength(4096);

        b.HasIndex(a => a.ServiceOrderId);
        b.HasIndex(a => a.PerformedBy);
        b.HasIndex(a => a.PerformedAt);
    }
}
