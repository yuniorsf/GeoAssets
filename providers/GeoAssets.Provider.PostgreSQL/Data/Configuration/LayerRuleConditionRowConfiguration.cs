using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Provider.PostgreSQL.Data.Configuration;

internal sealed class LayerRuleConditionRowConfiguration : IEntityTypeConfiguration<LayerRuleConditionRow>
{
    public void Configure(EntityTypeBuilder<LayerRuleConditionRow> e)
    {
        e.ToTable("layer_rule_condition");
        e.HasKey(x => x.Id);

        e.Property(x => x.Attribute).HasMaxLength(128).IsRequired();
        e.Property(x => x.Value).HasMaxLength(512).IsRequired();

        e.HasOne(x => x.LayerRule).WithMany(x => x.Conditions).HasForeignKey(x => x.LayerRuleId).OnDelete(DeleteBehavior.Cascade);

        e.HasIndex(x => x.LayerRuleId);
    }
}
