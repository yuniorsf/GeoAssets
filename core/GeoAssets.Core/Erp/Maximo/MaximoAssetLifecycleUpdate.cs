namespace GeoAssets.Core.Erp.Maximo;

/// <summary>
/// One IBM Maximo MIF asset lifecycle state change: decommission <see cref="OldSerial"/> and
/// activate <see cref="NewSerial"/> at <see cref="LocationFeatureId"/>, derived from a single
/// asset-swap <c>AssetMutationBatchRequestedEvent</c> (XD01-232).
/// </summary>
public sealed record MaximoAssetLifecycleUpdate(
    Guid BatchId,
    string OldSerial,
    string NewSerial,
    string LocationFeatureId,
    Guid OrganizationId,
    string? SourceOrderId,
    DateTimeOffset SyncedAtUtc);
