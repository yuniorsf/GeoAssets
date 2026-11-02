namespace GeoAssets.Workflow.Contractors;

/// <summary>
/// Result of <see cref="EspelhoDeMedicaoDossierGenerator.Generate"/> (XD01-249) — the full
/// pre-billing measurement dossier a concessionaire auditor clears an invoice against.
///
/// <see cref="DocumentText"/> is the dossier content itself: plain, deterministic text, not a
/// true PDF/A binary. No PDF/document-generation library is a dependency of this project yet
/// (confirmed: no reference anywhere in the solution) — same situation
/// <c>PoleLegalDefenseDossierGenerator</c> (XD01-286) hit and resolved the same way. Adding one
/// is a real, separate decision (which library, PDF/A conformance level, embedded-font
/// licensing) this ticket's own text doesn't make for me, so this produces the dossier's exact
/// content/signature deterministically and leaves PDF/A container generation to whichever host
/// layer eventually adds that dependency, exactly as the ISP precedent does for its own zip
/// packaging.
/// </summary>
public sealed record EspelhoDeMedicaoDossierResult(
    string ServiceOrderId,
    Guid ContractorOrganizationId,
    string ContractorName,
    string ServiceOrderTitle,
    DateTime ExecutionStartedAtUtc,
    DateTime? ExecutionCompletedAtUtc,
    IReadOnlyList<EspelhoDeMedicaoItemLine> Items,
    decimal TotalBrl,
    decimal RetentionRate,
    decimal RetentionBrl,
    decimal NetPayableBrl,
    string DocumentText,
    string SignatureBase64);
