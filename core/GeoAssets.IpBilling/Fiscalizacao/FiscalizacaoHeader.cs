namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// The Fiscalização-specific header row for one <c>ServiceOrder</c> (<c>OrderTypeId ==
/// "ip-fiscalizacao"</c>) — same denormalized-out-of-<c>ServiceOrder.Attributes</c> shape as
/// <c>Levantamento.LevantamentoHeader</c>, per the parent epic's "line/operation detail data
/// lives in dedicated GeoAssets.IpBilling tables keyed by ServiceOrder.Id" architecture
/// decision (XD01-175).
/// </summary>
public sealed class FiscalizacaoHeader
{
    /// <summary>Equal to the owning <c>ServiceOrder.Id</c> — a 1:1 relationship, not a separate identity.</summary>
    public required string ServiceOrderId { get; set; }

    public required Guid MunicipioId { get; set; }

    /// <summary>Billing period this spot-check inspection is for, "yyyy-MM".</summary>
    public required string Competencia { get; set; }

    /// <summary>Denormalized off the owning <c>ServiceOrder.Status</c> — set <c>true</c> when the order reaches <c>Validado</c>.</summary>
    public bool IsValidated { get; set; }
}
