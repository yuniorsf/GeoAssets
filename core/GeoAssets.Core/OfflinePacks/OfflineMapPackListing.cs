namespace GeoAssets.Core.OfflinePacks;

/// <summary>The offline map packs stored on this device and the storage they use (XD01-464).</summary>
/// <param name="Packs">Every stored pack, newest import first.</param>
/// <param name="TotalBytes">The bytes the stored packs use together.</param>
public sealed record OfflineMapPackListing(IReadOnlyList<OfflineMapPack> Packs, long TotalBytes);
