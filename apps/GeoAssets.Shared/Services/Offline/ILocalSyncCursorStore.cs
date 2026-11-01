namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// Persists the UTC timestamp of the last successful pull from
/// <see cref="IFieldServiceSyncClient"/> (XD01-243), so <see cref="MauiSyncGateway"/> only pulls
/// deltas since that point instead of every order on every sync. Defaults to
/// <see cref="DateTime.MinValue"/> on a brand-new device (pull everything).
/// </summary>
public interface ILocalSyncCursorStore
{
    Task<DateTime> GetLastSyncedUtcAsync(CancellationToken ct = default);

    Task SetLastSyncedUtcAsync(DateTime lastSyncedUtc, CancellationToken ct = default);
}
