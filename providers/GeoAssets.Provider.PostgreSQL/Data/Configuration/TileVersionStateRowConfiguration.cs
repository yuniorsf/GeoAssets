using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Provider.PostgreSQL.Data.Configuration;

internal sealed class TileVersionStateRowConfiguration : IEntityTypeConfiguration<TileVersionStateRow>
{
    public void Configure(EntityTypeBuilder<TileVersionStateRow> e)
    {
        e.ToTable("tile_version_state", t => t.HasCheckConstraint("CK_tile_version_state_singleton", "\"Id\" = 1"));
        e.HasKey(x => x.Id);
        e.Property(x => x.Id).ValueGeneratedNever();
        e.Property(x => x.Epoch).HasMaxLength(16).IsRequired();
        e.Property(x => x.Flushes).IsRequired();
    }
}
