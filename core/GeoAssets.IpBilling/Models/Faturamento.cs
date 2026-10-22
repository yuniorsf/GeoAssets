namespace GeoAssets.IpBilling.Models;

/// <summary>
/// One monthly billing ledger entry for a <see cref="Municipio"/> — the running "Memória de
/// Cálculo" figure that Phase 1's Levantamento calculation needs to read a prior
/// competência's value from, one phase before this epic's own write path exists.
///
/// Schema-only in this ticket (XD01-176): no business logic writes these rows yet.
/// Phase 4 (XD01-188) adds real auto-posting — SET from a validated Levantamento
/// (recomputes the whole baseline from scratch) and ACCUMULATE from validated
/// Fiscalização/Ofício deltas, posted synchronously in the same DB transaction as each
/// workflow's Validate action rather than through the eventually-consistent
/// <c>IOrderEventPublisher</c> pub-sub (wrong failure mode for money, per the parent epic).
/// </summary>
public sealed class Faturamento
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required Guid MunicipioId { get; set; }

    /// <summary>Billing period, "yyyy-MM" (e.g. "2026-03").</summary>
    public required string Competencia { get; set; }

    /// <summary>Billable kWh posted for this município/competência.</summary>
    public decimal KwhFaturado { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
