namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// Persistence abstraction for a Fiscalização's header/line graph. Deliberately smaller than
/// <c>Levantamento.ILevantamentoRepository</c>: Fiscalização has no cross-time
/// "new-vs-existing transformer" detection, so none of that repository's history-anchor queries
/// apply here (XD01-182).
/// </summary>
public interface IFiscalizacaoRepository
{
    Task<FiscalizacaoHeader?> GetHeaderAsync(string serviceOrderId, CancellationToken ct = default);

    Task AddHeaderAsync(FiscalizacaoHeader header, CancellationToken ct = default);

    Task MarkValidatedAsync(string serviceOrderId, CancellationToken ct = default);

    Task<IReadOnlyList<FiscalizacaoLine>> GetLinesAsync(
        string serviceOrderId, CancellationToken ct = default);

    /// <summary>Recalculation is idempotent per order — this deletes any previously-persisted
    /// lines for <paramref name="serviceOrderId"/> and inserts <paramref name="lines"/> in
    /// their place.</summary>
    Task ReplaceLinesAsync(
        string serviceOrderId, IReadOnlyList<FiscalizacaoLine> lines, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
