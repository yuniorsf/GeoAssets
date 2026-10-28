using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Repositories;

/// <summary>
/// Persistence abstraction for the <see cref="Faturamento"/> ledger. Schema-only in this
/// ticket (XD01-176) — <see cref="AddAsync"/> exists so the table round-trips in tests, but
/// no business logic calls it yet. Phase 1 (XD01-179) is the first real reader (a
/// Levantamento's "Memória de Cálculo" display needs a prior competência's billed figure);
/// Phase 4 (XD01-188) is the first real writer (auto-posting).
/// </summary>
public interface IFaturamentoRepository
{
    Task<Faturamento?> GetByMunicipioAndCompetenciaAsync(
        Guid municipioId, string competencia, CancellationToken ct = default);

    Task<IReadOnlyList<Faturamento>> GetByMunicipioAsync(Guid municipioId, CancellationToken ct = default);

    Task AddAsync(Faturamento faturamento, CancellationToken ct = default);

    /// <summary>
    /// Posts the validated order's kWh to the <see cref="Faturamento"/> ledger for
    /// <paramref name="municipioId"/> and <paramref name="competencia"/> using either
    /// <see cref="FaturamentoPostingMode.Set"/> (Levantamento baseline) or
    /// <see cref="FaturamentoPostingMode.Accumulate"/> (Fiscalização/Ofício delta).
    /// Idempotent per <c>(serviceOrderId, competencia)</c>.
    /// </summary>
    Task<Faturamento> PostOrderCalculationAsync(
        string serviceOrderId,
        Guid municipioId,
        string competencia,
        decimal kwhDeltaOrBaseline,
        FaturamentoPostingMode mode,
        DateTimeOffset postedAt,
        CancellationToken ct = default);

    Task<IReadOnlyList<FaturamentoPosting>> GetPostingsByOrderAsync(
        string serviceOrderId, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
