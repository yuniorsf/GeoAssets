namespace GeoAssets.Core.Erp.Maximo;

/// <summary>Result of <see cref="MaximoAssetLifecycleSyncService.SyncIfSwapAsync"/>.</summary>
public enum MaximoSyncOutcome
{
    /// <summary>The batch did not represent a recognizable asset swap — nothing to sync.</summary>
    NotASwap = 0,

    /// <summary>This batch's swap was already dispatched to Maximo on a prior delivery — skipped.</summary>
    AlreadyDispatched = 1,

    /// <summary>The swap was dispatched to Maximo on this call.</summary>
    Dispatched = 2,
}
