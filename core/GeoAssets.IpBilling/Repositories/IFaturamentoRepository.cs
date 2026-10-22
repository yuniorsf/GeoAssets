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

    Task SaveChangesAsync(CancellationToken ct = default);
}
