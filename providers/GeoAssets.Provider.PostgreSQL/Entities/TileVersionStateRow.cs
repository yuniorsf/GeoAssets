namespace GeoAssets.Provider.PostgreSQL.Entities;

/// <summary>
/// EF Core entity that maps to the one-row <c>tile_version_state</c> table — backs
/// <see cref="Repositories.PostgresTileVersionStore"/> (XD01-458): the epoch every kernel replica's tile URLs share (chosen at
/// random by the first replica to read it) and the flush count, both behind <c>{v}</c>'s cache version.
/// </summary>
public sealed class TileVersionStateRow
{
    /// <summary>The one row's id.</summary>
    public const short SingletonId = 1;

    /// <summary>Always <see cref="SingletonId"/>.</summary>
    public short Id { get; set; }
    public string Epoch { get; set; } = string.Empty;
    public long Flushes { get; set; }
}
