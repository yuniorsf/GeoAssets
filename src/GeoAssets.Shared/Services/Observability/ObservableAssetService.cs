using GeoAssets.Core.Diagnostics;
using GeoAssets.Core.Services;
using Microsoft.Extensions.Logging;

namespace GeoAssets.Shared.Services.Observability;

/// <summary>
/// Observable decorator for <see cref="IAssetService"/>.
/// Instruments <see cref="ImportAsync"/> with an OpenTelemetry span,
/// a duration metric, and structured logging; all other calls are
/// forwarded to the inner service without overhead.
/// </summary>
public sealed class ObservableAssetService(
    IAssetService inner,
    ILogger<ObservableAssetService> logger)
    : ObservableDecoratorBase<ObservableAssetService>(logger), IAssetService
{
    public string CollectionName
    {
        get => inner.CollectionName;
        set => inner.CollectionName = value;
    }

    public Task InitializeAsync()                            => inner.InitializeAsync();
    public Task<string> ExportAsync()                        => inner.ExportAsync();
    public Task ClearAllAsync(CancellationToken ct = default) => inner.ClearAllAsync(ct);
    public ValueTask DisposeAsync()                          => inner.DisposeAsync();

    public Task ImportAsync(string geoJson) =>
        TrackAsync(
            "import.parse_and_merge",
            () => inner.ImportAsync(geoJson),
            before: span => span?.SetTag("payload.bytes", geoJson.Length),
            after: (_, elapsedMs) =>
            {
                ImportDiagnostics.ParseDurationMs.Record(elapsedMs);
                Logger.LogInformation(
                    "AssetService.ImportAsync — {ElapsedMs} ms, payload {PayloadBytes} B",
                    elapsedMs, geoJson.Length);
            });
}
