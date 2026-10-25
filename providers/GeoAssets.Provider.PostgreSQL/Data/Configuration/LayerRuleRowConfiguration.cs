using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Provider.PostgreSQL.Data.Configuration;

internal sealed class LayerRuleRowConfiguration : IEntityTypeConfiguration<LayerRuleRow>
{
    public void Configure(EntityTypeBuilder<LayerRuleRow> e)
    {
        e.ToTable("layer_rule");
        e.HasKey(x => x.Id);

        e.HasOne(x => x.AssetType).WithMany().HasForeignKey(x => x.AssetTypeId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Layer).WithMany().HasForeignKey(x => x.LayerId).OnDelete(DeleteBehavior.Cascade);

        e.HasIndex(x => x.AssetTypeId);
        e.HasIndex(x => x.LayerId);
    }
}
