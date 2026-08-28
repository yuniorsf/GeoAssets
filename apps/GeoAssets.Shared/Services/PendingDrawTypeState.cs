using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services;

/// <summary>Scoped default implementation of <see cref="IPendingDrawTypeState"/>.</summary>
public sealed class PendingDrawTypeState : IPendingDrawTypeState
{
    public string? AssetTypeId { get; private set; }

    public void Set(string assetTypeId) => AssetTypeId = assetTypeId;
    public void Clear() => AssetTypeId = null;
}
