namespace GeoAssets.Workflow.Contractors.Governance;

/// <summary>
/// Outbound boundary to the utility's contractor governance/clearinghouse portal (SAP WeBUY /
/// CEMIG Portal de Fornecedores, XD01-238) — Core-owned interface, host-layer HTTP
/// implementation, same split as <c>IGeoCoreClient</c>/<c>IBssOssOutboundWebhookDispatcher</c>.
/// </summary>
public interface IContractorGovernancePortalClient
{
    /// <summary>Pushes a signed measurement sheet for an Inspected order (AC1).</summary>
    Task PushMeasurementSheetAsync(MeasurementSheetPortalPayload payload, CancellationToken ct = default);

    /// <summary>Queries a capital-works contract's remaining balance (AC2).</summary>
    Task<ContractBalanceQueryResult> QueryContractBalanceAsync(string contractId, CancellationToken ct = default);
}
