namespace GeoAssets.Core.Erp.Maximo;

/// <summary>
/// Sends an asset lifecycle update to IBM Maximo's Integration Framework (MIF). Segregated from
/// <see cref="MaximoAssetLifecycleSyncService"/> (which only extracts the swap and enforces
/// exactly-once dispatch, no I/O) so the transport can change independently — same split as
/// <c>Workflow.Erp.Sap.ISapPmConfirmationDispatcher</c>.
/// </summary>
public interface IMaximoAssetLifecycleDispatcher
{
    Task DispatchAsync(MaximoAssetLifecycleUpdate update, CancellationToken ct = default);
}
