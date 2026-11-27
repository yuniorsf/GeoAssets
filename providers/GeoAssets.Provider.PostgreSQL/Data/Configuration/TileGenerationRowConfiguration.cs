using GeoAssets.Provider.PostgreSQL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoAssets.Provider.PostgreSQL.Data.Configuration;

internal sealed class TileGenerationRowConfiguration : IEntityTypeConfiguration<TileGenerationRow>
{
    public void Configure(EntityTypeBuilder<TileGenerationRow> e)
    {
        e.ToTable("tile_generations");
        e.HasKey(x => x.Scope);
        e.Property(x => x.Scope).ValueGeneratedNever();
        e.Property(x => x.Generation).IsRequired();
    }
}
