using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.ApplicationModel;

namespace GeoAssets.MAUI.Services;

/// <summary>
/// Loads the order types <c>GeoAssets.Server</c> lists — the ones it registers in code (shared
/// pole use among them) with the persisted ones (XD01-378) — into this app's
/// <see cref="OrderTypeRegistry"/>, as <c>GeoAssets.Web</c>'s <c>Program.cs</c> does before it
/// renders. The client registers no vertical's order types of its own.
///
/// The types are fetched off the UI thread and registered on it, where the Blazor WebView renders
/// from the registry, so a render never reads it while it changes. Every fetched type's target
/// policy is validated before any is registered, as <see cref="OrderTypeRegistry.LoadFromAsync"/>
/// does. A failure — no signed-in account yet, no connectivity — is logged and leaves the
/// built-in defaults; the next start loads the server's types.
/// </summary>
internal sealed partial class OrderTypeCatalogLoader(
    IOrderTypeRepository repository, OrderTypeRegistry registry, ILogger<OrderTypeCatalogLoader> logger)
{
    /// <summary>Call once at app startup.</summary>
    public void Start() => _ = LoadAsync();

    private async Task LoadAsync()
    {
        try
        {
            var types = await repository.GetAllAsync().ConfigureAwait(false);
            foreach (var type in types)
                type.TargetPolicy?.Validate(type.Id);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                foreach (var type in types)
                    registry.Register(type);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogLoadFailed(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the server's order types; continuing with the built-in defaults.")]
    private static partial void LogLoadFailed(ILogger logger, Exception ex);
}
