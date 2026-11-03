namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// Orchestrates one Ofício's <c>Carregado -&gt; Calculado -&gt; Validado</c> workflow — Phase 3's
/// reuse of the pattern <c>Levantamento.ILevantamentoCalculationService</c>/
/// <c>Fiscalizacao.IFiscalizacaoCalculationService</c> established in Phases 1/2, plus the
/// duplicate-número guard and supersede path this ticket adds (XD01-185).
/// </summary>
public interface IOficioCalculationService
{
    /// <summary>
    /// Lands already-validated spreadsheet rows as lines — no Agregado/Retroativo computed yet,
    /// letting a human fix bad rows (by re-uploading) before <see cref="CalculateAsync"/> ever
    /// runs. Only valid while the order is still <c>Carregado</c>; re-uploading replaces
    /// whatever was there before.
    ///
    /// On the FIRST upload for this order (i.e. no header exists yet), if
    /// <paramref name="numeroPrefeitura"/> is non-null and already claimed by a different,
    /// still-active order in the same município, this throws
    /// <see cref="DuplicateOficioNumeroException"/> rather than silently proceeding — call
    /// <see cref="SupersedeAsync"/> on the existing order first for the legitimate correction
    /// case. A null <paramref name="numeroPrefeitura"/> is never considered a duplicate.
    /// </summary>
    Task<int> UploadAsync(
        string serviceOrderId,
        string? numeroPrefeitura,
        IReadOnlyList<OficioWorkbookRow> rows,
        CancellationToken ct = default);

    /// <summary>
    /// <c>Carregado -&gt; Calculado</c> (or an idempotent recompute while already
    /// <c>Calculado</c>): maps <paramref name="rows"/> into lines, computes each line's
    /// Agregado/Retroativo via <see cref="OficioLineCalculator"/>, derives each line's swap
    /// ("troca") flag via <see cref="OficioLineSwapView"/>, and persists the result.
    /// </summary>
    Task<OficioCalculationResult> CalculateAsync(
        string serviceOrderId,
        IReadOnlyList<OficioWorkbookRow> rows,
        string? performedBy = null,
        CancellationToken ct = default);

    /// <summary>
    /// Same as <see cref="CalculateAsync"/>, but re-derives rows from whatever
    /// <see cref="UploadAsync"/> most recently persisted instead of taking them as a parameter.
    /// </summary>
    Task<OficioCalculationResult> CalculateStoredAsync(
        string serviceOrderId,
        string? performedBy = null,
        CancellationToken ct = default);

    /// <summary>
    /// <c>Calculado -&gt; Validado</c>: marks this Ofício as validated. Deliberately does
    /// <em>not</em> post to the Faturamento ledger — accumulate-mode auto-posting from a
    /// validated Ofício is XD01-188's deliverable, not this ticket's.
    /// </summary>
    Task<OficioValidationResult> ValidateAsync(
        string serviceOrderId,
        string? performedBy = null,
        CancellationToken ct = default);

    /// <summary>
    /// The legitimate correction path for a duplicate <c>NumeroPrefeitura</c>: releases
    /// <paramref name="serviceOrderId"/>'s claim on its current número (sets it to <c>null</c>)
    /// and explicitly logs the action against that order, so a corrective re-upload on another
    /// order can claim the same número without a <see cref="DuplicateOficioNumeroException"/>.
    /// Throws <see cref="InvalidOperationException"/> if the order's header has no número to
    /// release.
    /// </summary>
    Task SupersedeAsync(
        string serviceOrderId,
        string? performedBy = null,
        CancellationToken ct = default);
}
