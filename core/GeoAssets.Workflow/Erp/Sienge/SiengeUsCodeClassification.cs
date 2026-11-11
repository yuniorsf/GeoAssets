namespace GeoAssets.Workflow.Erp.Sienge;

/// <summary>
/// This codebase has no equipment-hour/hauling-ticket ledger distinct from the Unidade de
/// Serviço (US) billing ledger (same gap <c>Erp.Totvs.IFieldMaterialConsumptionSource</c>'s doc
/// comment notes for material consumption). Civil crews classify their own ledger's
/// <see cref="Contractors.ContractorUsLedgerEntry.UsCode"/> values into these three disjoint
/// sets via configuration, rather than this connector guessing/hardcoding specific codes.
/// </summary>
public sealed class SiengeUsCodeClassification
{
    public const string SectionName = "WorkflowSiengeSync:UsCodeClassification";

    public HashSet<string> MachineHourUsCodes { get; init; } = [];

    public HashSet<string> HaulingTicketUsCodes { get; init; } = [];

    public HashSet<string> MaterialConsumptionUsCodes { get; init; } = [];
}
