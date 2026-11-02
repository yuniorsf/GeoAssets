namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// The legacy GEOS_FAT_IP "Tipo de Retroação" code assigned to a new transformer detected on a
/// Levantamento (XD01-179) — six legacy numeric codes (00/01/10/11/20/30), only ever persisted
/// on <see cref="LevantamentoTransformerLine"/> rows for transformers flagged
/// <see cref="LevantamentoTransformerLine.IsNewTransformer"/>; existing transformers carry no
/// code (<c>null</c>).
///
/// <strong>Reconstruction notice:</strong> the real GEOS_FAT_IP legacy specification documents
/// (Espec_*) that would define these six codes authoritatively are not available in this
/// repository or environment. The mapping below is a best-effort reconstruction from this
/// ticket's own prose description (XD01-179's "Calculation" section and its business-owner
/// checkpoints) — internally consistent and fully testable, but **not verified against the
/// real legacy system** and flagged as such on the ticket. Confirm against an authoritative
/// Espec_* document or the business owner before this is treated as ground truth.
///
/// Reconstructed decision tree (see <see cref="LevantamentoRetroactiveCalculator"/>):
/// <list type="bullet">
///   <item><description><c>30</c> — the município's very first validated Levantamento ever; every
///   transformer counts as new (working default) but there is no baseline to retroact against.</description></item>
///   <item><description><c>20</c> — a transformer never seen in ANY prior validated Levantamento
///   (not just missing from the most recent one) — anchors at the município's FIRST validated
///   Levantamento competência (oldest available baseline), capped at 730 days.</description></item>
///   <item><description><c>00</c>/<c>01</c> — a transformer that dropped out and reappears as "new"
///   relative to the most recent validated Levantamento, with no validated Fiscalização/Ofício on
///   record for it: anchors at the most recent validated Levantamento. <c>01</c> is the
///   <strong>[FIX]</strong> variant — it excludes kWh an intervening validated Fiscalização/Ofício
///   already billed for this transformer within the window; <c>00</c> is the naive case (no
///   intervening operation was found, so nothing to exclude).</description></item>
///   <item><description><c>10</c>/<c>11</c> — same reappearance scenario, but a validated
///   Fiscalização/Ofício record exists for the transformer, so the window anchors there instead
///   (the "10/11 anchor" business-owner checkpoint: município-wide vs. transformer-specific
///   Fiscalização — see XD01-175). <c>11</c> is the <strong>[FIX]</strong> variant (excludes
///   already-billed kWh); <c>10</c> is the naive legacy-faithful case.</description></item>
/// </list>
/// </summary>
public enum TipoRetroacao
{
    Naive00 = 0,
    FixExclusao01 = 1,
    FiscalizacaoNaive10 = 10,
    FiscalizacaoFixExclusao11 = 11,
    HistoricoCompleto20 = 20,
    PrimeiraLevantamentoDoMunicipio30 = 30,
}
