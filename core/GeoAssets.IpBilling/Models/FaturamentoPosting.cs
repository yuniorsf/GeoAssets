namespace GeoAssets.IpBilling.Models;

/// <summary>
/// Idempotency and audit record for each <see cref="Faturamento"/> ledger posting triggered by
/// validating an IP-billing <c>ServiceOrder</c>. Enforces <c>Unique(ServiceOrderId, Competencia)</c>
/// so retrying a <see cref="FaturamentoPostingMode.Accumulate"/> validation never double-counts kWh.
/// </summary>
public sealed class FaturamentoPosting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string ServiceOrderId { get; set; }

    public required Guid MunicipioId { get; set; }

    /// <summary>Billing period, "yyyy-MM" (e.g. "2026-03").</summary>
    public required string Competencia { get; set; }

    public required FaturamentoPostingMode Mode { get; set; }

    public decimal DeltaOrBaselineKwh { get; set; }

    public decimal ResultingKwh { get; set; }

    public DateTimeOffset PostedAt { get; set; }

    /// <summary>
    /// <c>true</c> when <see cref="DeltaOrBaselineKwh"/> came from a provisional Fiscalização/
    /// Ofício conversion — at least one point had no lamp detail and was converted with the
    /// applicable chart's mean watts-per-point instead of its real lamp wattage (XD01-301) — so
    /// the posting can be found and recalculated later. Always <c>false</c> for Levantamento
    /// (<see cref="FaturamentoPostingMode.Set"/>) and manual (<see cref="FaturamentoPostingMode.Overwrite"/>)
    /// postings.
    /// </summary>
    public bool IsProvisional { get; set; }
}
