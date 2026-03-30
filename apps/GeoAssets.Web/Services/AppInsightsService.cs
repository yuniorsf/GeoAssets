using GeoAssets.Shared.Services;
using Microsoft.JSInterop;

namespace GeoAssets.Web.Services;

/// <summary>
/// Sends custom telemetry to Azure Application Insights via the JS SDK.
/// All methods are fire-and-forget — telemetry never blocks the caller.
///
/// Severity levels (trackTrace): 0=Verbose, 1=Information, 2=Warning, 3=Error, 4=Critical
/// </summary>
public sealed class AppInsightsService(IJSRuntime js) : IAnalyticsService
{
    private const string Ns = "appInsightsInterop";

    /// <summary>
    /// Records a named custom event with optional property bag.
    /// Visible in Azure Monitor → Logs → <c>customEvents</c> table.
    /// </summary>
    public void TrackEvent(string name, object? properties = null) =>
        _ = js.InvokeVoidAsync($"{Ns}.trackEvent", name, properties);

    /// <summary>
    /// Records a diagnostic trace message.
    /// Visible in Azure Monitor → Logs → <c>traces</c> table.
    /// </summary>
    public void TrackTrace(string message, int severityLevel = 1, object? properties = null) =>
        _ = js.InvokeVoidAsync($"{Ns}.trackTrace", message, severityLevel, properties);

    /// <summary>
    /// Records an exception.
    /// Visible in Azure Monitor → Logs → <c>exceptions</c> table.
    /// </summary>
    public void TrackException(string message, object? properties = null) =>
        _ = js.InvokeVoidAsync($"{Ns}.trackException", message, properties);

    /// <summary>
    /// Records a single numeric measurement.
    /// Visible in Azure Monitor → Logs → <c>customMetrics</c> table.
    /// </summary>
    public void TrackMetric(string name, double value, object? properties = null) =>
        _ = js.InvokeVoidAsync($"{Ns}.trackMetric", name, value, properties);

    /// <summary>
    /// Associates subsequent telemetry with an authenticated user identity.
    /// </summary>
    public void SetUser(string userId) =>
        _ = js.InvokeVoidAsync($"{Ns}.setUser", userId);
}
