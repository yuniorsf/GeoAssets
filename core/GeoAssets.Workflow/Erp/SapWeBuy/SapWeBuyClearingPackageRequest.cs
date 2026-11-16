using GeoAssets.Workflow.Contractors;

namespace GeoAssets.Workflow.Erp.SapWeBuy;

/// <summary>
/// Input to <see cref="SapWeBuyClearingPackageExporter.Export"/> (XD01-255). <see cref="Dossier"/>
/// is an already-generated, already-signed <see cref="EspelhoDeMedicaoDossierResult"/> (XD01-249)
/// — this adapter compiles it into the clearing-package wire format, it never re-derives totals.
///
/// <see cref="PoNumber"/> and <see cref="OutlineAgreementNumber"/> are SAP-side procurement
/// identifiers with no equivalent concept anywhere in <c>GeoAssets.Workflow</c>'s own domain
/// model (confirmed: no "purchase order"/"outline agreement" field exists on any contract/order
/// type) — they are assigned when the contract/PO is created in SAP and must be supplied by the
/// caller, the same way <c>MeasurementSheetPortalPayload.FromDossier</c> (XD01-238) takes its own
/// <c>contractId</c> as an explicit parameter rather than resolving one internally.
/// </summary>
public sealed record SapWeBuyClearingPackageRequest(
    EspelhoDeMedicaoDossierResult Dossier,
    string PoNumber,
    string OutlineAgreementNumber,
    string EvidenceDeepLinkBaseUrl);
