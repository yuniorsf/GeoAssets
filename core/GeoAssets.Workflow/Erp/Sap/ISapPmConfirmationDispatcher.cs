namespace GeoAssets.Workflow.Erp.Sap;

/// <summary>
/// Sends an order confirmation to SAP. Segregated from <see cref="SapPmOrderConfirmationDispatchService"/>
/// (which only builds the payload and enforces exactly-once dispatch, no I/O) so the transport —
/// a direct BAPI/OData call today, possibly a queue later — can change independently. Same split
/// as <see cref="Isp.Bss.IBssOssOutboundWebhookDispatcher"/>.
/// </summary>
public interface ISapPmConfirmationDispatcher
{
    Task DispatchAsync(SapPmOrderConfirmation confirmation, CancellationToken ct = default);
}
