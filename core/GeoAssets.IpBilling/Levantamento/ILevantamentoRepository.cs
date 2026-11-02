namespace GeoAssets.IpBilling.Levantamento;

/// <summary>Persistence abstraction for a Levantamento's header/transformer-lines/24h-lines graph.</summary>
public interface ILevantamentoRepository
{
    Task<LevantamentoHeader?> GetHeaderAsync(string serviceOrderId, CancellationToken ct = default);

    Task AddHeaderAsync(LevantamentoHeader header, CancellationToken ct = default);

    Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default);

    Task<IReadOnlyList<LevantamentoTransformerLine>> GetTransformerLinesAsync(
        string serviceOrderId, CancellationToken ct = default);

    /// <summary>Recalculation is idempotent per order — this deletes any previously-persisted
    /// transformer lines for <paramref name="serviceOrderId"/> and inserts <paramref name="lines"/>
    /// in their place.</summary>
    Task ReplaceTransformerLinesAsync(
        string serviceOrderId, IReadOnlyList<LevantamentoTransformerLine> lines, CancellationToken ct = default);

    Task<IReadOnlyList<Levantamento24hLine>> Get24hLinesAsync(
        string serviceOrderId, CancellationToken ct = default);

    /// <summary>Same idempotent-replace semantics as <see cref="ReplaceTransformerLinesAsync"/>.</summary>
    Task Replace24hLinesAsync(
        string serviceOrderId, IReadOnlyList<Levantamento24hLine> lines, CancellationToken ct = default);

    /// <summary>
    /// The município's most recently <em>validated</em> Levantamento strictly before
    /// <paramref name="beforeCompetencia"/> ("yyyy-MM", exclusive) — the "new transformer"
    /// comparison baseline and the 00/01 retroactive-window anchor. <c>null</c> when the
    /// município has no validated Levantamento at all yet.
    /// </summary>
    Task<LevantamentoHeader?> GetMostRecentValidatedAsync(
        Guid municipioId, string beforeCompetencia, CancellationToken ct = default);

    /// <summary>
    /// The município's very first (oldest competência) validated Levantamento — the code-20
    /// retroactive-window anchor. <c>null</c> when the município has no validated Levantamento
    /// at all yet (equivalent to <see cref="GetMostRecentValidatedAsync"/> returning <c>null</c>).
    /// </summary>
    Task<LevantamentoHeader?> GetFirstValidatedAsync(Guid municipioId, CancellationToken ct = default);

    /// <summary>
    /// Every <see cref="LevantamentoTransformerLine.CodigoInstalacao"/> that has ever appeared
    /// on any validated Levantamento for this município — used to distinguish "never seen
    /// before" (code 20) from "missing only from the most recent survey" (codes 00/01/10/11).
    /// </summary>
    Task<IReadOnlySet<string>> GetAllValidatedCodigosInstalacaoAsync(
        Guid municipioId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
