using GeoAssets.Core.Interfaces;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Contractors.Governance;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Contractor governance portal REST API (SAP WeBUY / CEMIG Portal de Fornecedores, XD01-238):
/// pushing a signed measurement sheet for an Inspected order (AC1), and the pre-dispatch
/// contract-balance gate for capital work packages (AC2).
/// </summary>
internal static class ContractorGovernancePortalRestApiExtensions
{
    public static IEndpointRouteBuilder MapContractorGovernancePortalApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/fieldservice/contractor-governance-portal")
    {
        MapPushMeasurementSheet(routes, prefix);
        MapDispatchAuthorization(routes, prefix);
        return routes;
    }

    /// <summary>
    /// Generates the same signed Espelho de Medição dossier as
    /// <c>ContractorUsLedgerRestApiExtensions.MapEspelhoDeMedicaoDossier</c>, then pushes it to
    /// the portal as a <see cref="MeasurementSheetPortalPayload"/> — only for an order already in
    /// <see cref="FieldWorkStatus.Inspected"/> (AC1 is explicit this is an Inspected-order push).
    /// </summary>
    private static void MapPushMeasurementSheet(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapPost($"{prefix}/by-service-order/{{serviceOrderId}}/push-measurement-sheet", async (
            string serviceOrderId,
            PushMeasurementSheetRequest request,
            IServiceOrderRepository orderRepo,
            IContractorUsPostingRepository postingRepo,
            IContractorUsLedgerRepository ledgerRepo,
            IContractorGovernancePortalClient portalClient,
            CancellationToken ct) =>
        {
            var order = await orderRepo.GetByIdAsync(serviceOrderId, ct).ConfigureAwait(false);
            if (order is null) return Results.NotFound();

            if (!string.Equals(order.Status, FieldWorkStatus.Inspected, StringComparison.Ordinal))
            {
                return Results.BadRequest(
                    $"ServiceOrder '{serviceOrderId}' is not Inspected (current status: '{order.Status}'). " +
                    "Only Inspected orders can have their measurement sheet pushed to the portal.");
            }

            var postings = await postingRepo.GetByServiceOrderIdAsync(serviceOrderId, ct).ConfigureAwait(false);
            var ledgerEntries = await ledgerRepo.GetByServiceOrderIdAsync(serviceOrderId, ct).ConfigureAwait(false);

            var dossier = EspelhoDeMedicaoDossierGenerator.Generate(
                order, contractorName: null, postings, ledgerEntries, request.RetentionRate, request.TenantSecret);

            var payload = MeasurementSheetPortalPayload.FromDossier(dossier, request.ContractId);
            await portalClient.PushMeasurementSheetAsync(payload, ct).ConfigureAwait(false);

            return Results.Ok(payload);
        })
        .WithName("PushContractorGovernancePortalMeasurementSheet")
        .WithSummary("Pushes the signed measurement sheet for an Inspected order to the contractor governance portal.")
        .Produces<MeasurementSheetPortalPayload>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Pre-dispatch gate (AC2): queries the portal's own contract-balance endpoint and returns
    /// whether a capital work package of the given cost may be dispatched. A pure query — the
    /// caller is responsible for actually enforcing <see cref="CapitalWorkPackageDispatchAuthorization.Allowed"/>
    /// before invoking the order's own Dispatch action.
    /// </summary>
    private static void MapDispatchAuthorization(IEndpointRouteBuilder routes, string prefix)
    {
        routes.MapPost($"{prefix}/capital-work-packages/dispatch-authorization", async (
            CapitalWorkPackageDispatchAuthorizationRequest request,
            IContractorGovernancePortalClient portalClient,
            CancellationToken ct) =>
        {
            var balance = await portalClient.QueryContractBalanceAsync(request.ContractId, ct).ConfigureAwait(false);
            var authorization = CapitalWorkPackageDispatchAuthorizer.Authorize(request.RequiredAmountBrl, balance);
            return Results.Ok(authorization);
        })
        .WithName("AuthorizeCapitalWorkPackageDispatch")
        .WithSummary("Checks whether a capital work package may be dispatched, based on the contract's remaining portal balance.")
        .Produces<CapitalWorkPackageDispatchAuthorization>();
    }
}
