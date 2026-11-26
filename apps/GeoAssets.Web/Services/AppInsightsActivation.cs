using Microsoft.Extensions.Configuration;

namespace GeoAssets.Web.Services;

/// <summary>
/// Decides whether the browser's Application Insights SDK is initialized — the Web client's side of the hosts'
/// <c>Observability:ExportEnabled</c> master switch (XD01-440; XD01-451): browser telemetry leaves the page only when that switch
/// is <c>true</c> and <c>Observability:AzureMonitor:ConnectionString</c> is set.
/// </summary>
internal static class AppInsightsActivation
{
    internal const string ExportEnabledKey = "Observability:ExportEnabled";
    internal const string ConnectionStringKey = "Observability:AzureMonitor:ConnectionString";

    /// <summary>The connection string to initialize the SDK with, or <c>null</c> when browser telemetry stays off.</summary>
    internal static string? ConnectionStringToInitialize(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!bool.TryParse(configuration[ExportEnabledKey], out var exportEnabled) || !exportEnabled)
            return null;

        var connectionString = configuration[ConnectionStringKey];
        return string.IsNullOrWhiteSpace(connectionString) ? null : connectionString;
    }
}
