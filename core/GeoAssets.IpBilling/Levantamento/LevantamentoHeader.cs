namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// The Levantamento-specific header row for one <c>ServiceOrder</c> (<c>OrderTypeId ==
/// "ip-levantamento"</c>) — denormalized out of <c>ServiceOrder.Attributes</c> so
/// <see cref="MunicipioId"/>/<see cref="Competencia"/> are relationally queryable/joinable to
/// this ticket's <see cref="LevantamentoTransformerLine"/>/<see cref="Levantamento24hLine"/>
/// rows, per the parent epic's "line/operation detail data lives in dedicated
/// GeoAssets.IpBilling tables keyed by ServiceOrder.Id" architecture decision (XD01-175).
/// </summary>
public sealed class LevantamentoHeader
{
    /// <summary>Equal to the owning <c>ServiceOrder.Id</c> — a 1:1 relationship, not a separate identity.</summary>
    public required string ServiceOrderId { get; set; }

    public required Guid MunicipioId { get; set; }

    /// <summary>Billing period this survey is for, "yyyy-MM".</summary>
    public required string Competencia { get; set; }

    /// <summary>
    /// Denormalized off the owning <c>ServiceOrder.Status</c> — set <c>true</c> when the order
    /// reaches <c>Validado</c>. <see cref="ILevantamentoRepository"/>'s "most recent validated"/
    /// "first validated" queries need this filterable at the SQL level without a cross-DbContext
    /// join to <c>ServiceOrderDbContext</c> (this module's tables are a deliberately separate
    /// DbContext per the parent epic's architecture decision, XD01-175).
    /// </summary>
    public bool IsValidated { get; set; }
}
