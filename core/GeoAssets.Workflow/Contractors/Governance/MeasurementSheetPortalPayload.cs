namespace GeoAssets.Workflow.Contractors.Governance;

/// <summary>
/// The contractor clearinghouse portal's (SAP WeBUY / CEMIG Portal de Fornecedores, XD01-238
/// AC1) documented measurement-sheet push payload. Built from an already-generated
/// <see cref="EspelhoDeMedicaoDossierResult"/> (XD01-249) rather than re-deriving totals, so the
/// pushed figures are always exactly what the signed dossier itself states.
/// </summary>
public sealed record MeasurementSheetPortalPayload(
    string ContractId,
    string ServiceOrderId,
    Guid ContractorOrganizationId,
    string ContractorName,
    IReadOnlyList<MeasurementSheetPortalPayloadItem> Items,
    decimal TotalBrl,
    decimal RetentionBrl,
    decimal NetPayableBrl,
    string DossierSignatureBase64)
{
    public static MeasurementSheetPortalPayload FromDossier(EspelhoDeMedicaoDossierResult dossier, string contractId)
    {
        ArgumentNullException.ThrowIfNull(dossier);
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);

        var items = dossier.Items
            .Select(i => new MeasurementSheetPortalPayloadItem(i.ItemCode, i.Quantity, i.UnitUs, i.TotalBrl))
            .ToList();

        return new MeasurementSheetPortalPayload(
            contractId,
            dossier.ServiceOrderId,
            dossier.ContractorOrganizationId,
            dossier.ContractorName,
            items,
            dossier.TotalBrl,
            dossier.RetentionBrl,
            dossier.NetPayableBrl,
            dossier.SignatureBase64);
    }
}
