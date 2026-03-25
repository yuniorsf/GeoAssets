using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Workflow.Persistence.Configurations;

internal sealed class OrderCreationPolicyRecordConfiguration
    : IEntityTypeConfiguration<OrderCreationPolicyRecord>
{
    public void Configure(EntityTypeBuilder<OrderCreationPolicyRecord> b)
    {
        b.ToTable("OrderCreationPolicies");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedOnAdd();

        b.Property(p => p.OrderTypeId).IsRequired().HasMaxLength(128);
        b.Property(p => p.Value).IsRequired().HasMaxLength(256);

        b.HasIndex(p => p.OrderTypeId);
    }
}
