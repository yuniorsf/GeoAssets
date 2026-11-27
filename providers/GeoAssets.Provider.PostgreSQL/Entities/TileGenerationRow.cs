namespace GeoAssets.Provider.PostgreSQL.Entities;

/// <summary>
/// EF Core entity that maps to the <c>tile_generations</c> table — backs <see cref="Repositories.PostgresTileVersionStore"/>
/// (XD01-458): the tile generation of one scope, an organization id (<see cref="Guid.Empty"/> = the organization-less, shared
/// features), advanced by one per committed write that scope sees. A scope never advanced has no row (generation 0).
/// </summary>
public sealed class TileGenerationRow
{
    public Guid Scope { get; set; }
    public long Generation { get; set; }
}
