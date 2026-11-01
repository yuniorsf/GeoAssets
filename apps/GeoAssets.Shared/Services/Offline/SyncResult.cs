namespace GeoAssets.Shared.Services.Offline;

/// <summary>Outcome of one <see cref="MauiSyncGateway.SyncAsync"/> run.</summary>
public sealed record SyncResult(int Pushed, int Pulled, int Reconciled, int Failed, IReadOnlyList<string> Errors);
