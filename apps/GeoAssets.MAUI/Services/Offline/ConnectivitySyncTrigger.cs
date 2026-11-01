using GeoAssets.Shared.Services.Offline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Networking;

namespace GeoAssets.MAUI.Services.Offline;

/// <summary>
/// Triggers <see cref="MauiSyncGateway.SyncAsync"/> automatically when the device regains
/// internet access (XD01-276's AC: "connectivity-aware outbox flush on reconnect") — subscribes
/// to <see cref="Connectivity.ConnectivityChanged"/> and fires only on the transition <em>into</em>
/// <see cref="NetworkAccess.Internet"/>, not on every connectivity event while already online
/// (e.g. a Wi-Fi/cellular handoff that never drops internet access), so a flush isn't attempted
/// redundantly.
///
/// <see cref="MauiSyncGateway"/> is scoped (DI-registered by <see cref="OfflineSyncServiceExtensions.AddFieldServiceOfflineSync"/>);
/// this trigger is a singleton for the app's lifetime, so it resolves the gateway through a
/// fresh <see cref="IServiceScope"/> per reconnect rather than holding a captive scoped instance.
/// A flush failure is logged and swallowed — the outbox entries stay pending for the next
/// reconnect or manual sync, same "no partial, no silent retry storm" posture
/// <see cref="MauiSyncGateway"/> itself already documents.
/// </summary>
internal sealed partial class ConnectivitySyncTrigger(IServiceScopeFactory scopeFactory, ILogger<ConnectivitySyncTrigger> logger) : IDisposable
{
    private NetworkAccess _lastAccess = Connectivity.Current.NetworkAccess;

    /// <summary>Call once at app startup. Idempotent to call more than once (re-subscribing would double-fire, so don't).</summary>
    public void Start() => Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;

    public void Dispose() => Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;

    private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        var previousAccess = _lastAccess;
        _lastAccess = e.NetworkAccess;

        if (previousAccess == NetworkAccess.Internet || e.NetworkAccess != NetworkAccess.Internet)
            return;

        _ = FlushAsync();
    }

    private async Task FlushAsync()
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var gateway = scope.ServiceProvider.GetRequiredService<MauiSyncGateway>();
            await gateway.SyncAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogFlushFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Connectivity-triggered offline sync flush failed.")]
    private static partial void LogFlushFailed(ILogger logger, Exception ex);
}
