using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class OrderTypeRecipientActionRecordConfiguration : IEntityTypeConfiguration<OrderTypeRecipientActionRecord>
{
    public void Configure(EntityTypeBuilder<OrderTypeRecipientActionRecord> b)
    {
        b.ToTable("OrderTypeRecipientActions");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedOnAdd();

        b.Property(a => a.OrderTypeId).IsRequired().HasMaxLength(128);
        b.Property(a => a.FromStateKey).HasMaxLength(64);
        b.Property(a => a.ToStateKey).HasMaxLength(64);

        b.HasIndex(a => new { a.OrderTypeId, a.Action });
    }
}
