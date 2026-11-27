namespace GeoAssets.Core.OfflinePacks;

/// <summary>What <see cref="IOfflineMapPackCatalog.DeleteAsync"/> did (XD01-464).</summary>
public enum OfflineMapPackDeletion
{
    /// <summary>The pack, its cached tiles and the entries that drew it are gone.</summary>
    Deleted = 0,

    /// <summary>No pack with that id is stored; nothing changed.</summary>
    NotFound = 1,

    /// <summary>The pool's active workspace draws the pack, so nothing was deleted: another entry must be made active first.</summary>
    InUseByActiveEntry = 2,
}
