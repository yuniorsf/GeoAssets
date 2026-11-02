namespace GeoAssets.Workflow.Isp.Bss;

/// <summary>
/// Sends a provider-built activation payload back to that BSS/OSS vendor. Segregated from
/// <see cref="IBssOssOrderAdapter"/> (which only builds the payload, no I/O) so the dispatch
/// mechanism — HTTP today, possibly a queue later — can change without touching adapter logic.
/// </summary>
public interface IBssOssOutboundWebhookDispatcher
{
    Task DispatchAsync(string providerId, string payloadJson, CancellationToken ct = default);
}
