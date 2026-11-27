namespace GeoAssets.Core.OfflinePacks;

/// <summary>A provider-pool entry that draws a stored offline map pack (XD01-464).</summary>
/// <param name="EntryId">The entry's <see cref="Models.ProviderEntry.Id"/>.</param>
/// <param name="Name">The entry's name.</param>
/// <param name="IsActive">Whether the entry is the pool's active workspace, which a pack deletion doesn't remove.</param>
public sealed record OfflineMapPackUse(Guid EntryId, string Name, bool IsActive);
