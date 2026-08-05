using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class OrderTypeTransitionRecordConfiguration : IEntityTypeConfiguration<OrderTypeTransitionRecord>
{
    public void Configure(EntityTypeBuilder<OrderTypeTransitionRecord> b)
    {
        b.ToTable("OrderTypeTransitions");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).ValueGeneratedOnAdd();

        b.Property(t => t.OrderTypeId).IsRequired().HasMaxLength(128);
        b.Property(t => t.FromStateKey).IsRequired().HasMaxLength(64);
        b.Property(t => t.ToStateKey).IsRequired().HasMaxLength(64);

        b.HasIndex(t => new { t.OrderTypeId, t.FromStateKey });
    }
}
