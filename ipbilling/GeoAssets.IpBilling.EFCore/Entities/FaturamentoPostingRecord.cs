using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>FaturamentoPostings</c> idempotency/audit table.</summary>
internal sealed class FaturamentoPostingRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ServiceOrderId { get; set; } = string.Empty;

    public Guid MunicipioId { get; set; }

    public string Competencia { get; set; } = string.Empty;

    public FaturamentoPostingMode Mode { get; set; }

    public decimal DeltaOrBaselineKwh { get; set; }

    public decimal ResultingKwh { get; set; }

    public DateTimeOffset PostedAt { get; set; }
}
