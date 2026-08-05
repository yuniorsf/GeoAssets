using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class OrderTypeStateRecordConfiguration : IEntityTypeConfiguration<OrderTypeStateRecord>
{
    public void Configure(EntityTypeBuilder<OrderTypeStateRecord> b)
    {
        b.ToTable("OrderTypeStates");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedOnAdd();

        b.Property(s => s.OrderTypeId).IsRequired().HasMaxLength(128);
        b.Property(s => s.Key).IsRequired().HasMaxLength(64);
        b.Property(s => s.DisplayName).IsRequired().HasMaxLength(256);

        b.HasIndex(s => new { s.OrderTypeId, s.Key }).IsUnique();
    }
}
