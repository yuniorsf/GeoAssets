using GeoAssets.Core.Rendering;

namespace GeoAssets.Core.OfflinePacks;

/// <summary>
/// An offline map pack stored on this device (XD01-464), as <see cref="IOfflineMapPackCatalog.ListAsync"/> lists it.
/// </summary>
/// <param name="Id">The pack's id, what <see cref="IOfflineMapPackCatalog.DeleteAsync"/> takes.</param>
/// <param name="DisplayName">The name to show: the name of the first entry that draws it, else the picked file's name, else its id.</param>
/// <param name="FileName">The name of the file the pack was imported from, when it was recorded.</param>
/// <param name="Kind">What the pack holds.</param>
/// <param name="SizeBytes">The size of the stored file.</param>
/// <param name="ImportedAt">When the pack was imported onto this device.</param>
/// <param name="Bounds">The area the pack covers, from its header; <see langword="null"/> when the header gives none or can't be read.</param>
/// <param name="MinZoom">The pack's lowest zoom, from its header; <see langword="null"/> when the header can't be read.</param>
/// <param name="MaxZoom">The pack's highest zoom, from its header; <see langword="null"/> when the header can't be read.</param>
/// <param name="UsedBy">The provider-pool entries that draw it; empty when none does.</param>
public sealed record OfflineMapPack(
    string Id,
    string DisplayName,
    string? FileName,
    OfflineMapPackKind Kind,
    long SizeBytes,
    DateTimeOffset ImportedAt,
    BoundingBox? Bounds,
    int? MinZoom,
    int? MaxZoom,
    IReadOnlyList<OfflineMapPackUse> UsedBy);
