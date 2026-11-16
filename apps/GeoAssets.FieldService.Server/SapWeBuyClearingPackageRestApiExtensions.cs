using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.Erp.SapWeBuy;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.FieldService.Server;

/// <summary>
/// Maps the SAP WeBUY clearing-package export endpoint (XD01-255) onto any
/// <see cref="IEndpointRouteBuilder"/>. Thin HTTP façade over
/// <see cref="EspelhoDeMedicaoDossierGenerator"/> + <see cref="SapWeBuyClearingPackageExporter"/> —
/// same division of responsibility <see cref="ContractorUsLedgerRestApiExtensions"/> already uses
/// for the dossier-generation endpoint this one builds on.
/// </summary>
internal static class SapWeBuyClearingPackageRestApiExtensions
{
    public static IEndpointRouteBuilder MapSapWeBuyClearingPackageApi(
        this IEndpointRouteBuilder routes,
        string prefix = "/api/fieldservice/erp/sap-webuy")
    {
        routes.MapPost($"{prefix}/by-service-order/{{serviceOrderId}}/clearing-package", async (
            string serviceOrderId,
            SapWeBuyExportClearingPackageApiRequest request,
            IServiceOrderRepository orderRepo,
            IContractorUsPostingRepository postingRepo,
            IContractorUsLedgerRepository ledgerRepo,
            CancellationToken ct) =>
        {
            var order = await orderRepo.GetByIdAsync(serviceOrderId, ct).ConfigureAwait(false);
            if (order is null) return Results.NotFound();

            var postings = await postingRepo.GetByServiceOrderIdAsync(serviceOrderId, ct).ConfigureAwait(false);
            var ledgerEntries = await ledgerRepo.GetByServiceOrderIdAsync(serviceOrderId, ct).ConfigureAwait(false);

            var dossier = EspelhoDeMedicaoDossierGenerator.Generate(
                order, contractorName: null, postings, ledgerEntries, request.RetentionRate, request.TenantSecret);

            var packageRequest = new SapWeBuyClearingPackageRequest(
                dossier, request.PoNumber, request.OutlineAgreementNumber, request.EvidenceDeepLinkBaseUrl);

            var package = SapWeBuyClearingPackageExporter.Export(packageRequest);
            return Results.File(package.XmlBytes, "application/xml", package.FileName);
        })
        .WithName("ExportSapWeBuyClearingPackage")
        .WithSummary("Compiles an inspected order's Espelho de Medição dossier into a SAP WeBUY clearing-package XML.")
        .Produces(StatusCodes.Status200OK, contentType: "application/xml")
        .Produces(StatusCodes.Status404NotFound);

        return routes;
    }
}
