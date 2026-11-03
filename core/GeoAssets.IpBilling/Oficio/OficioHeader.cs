namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// The Ofício-specific header row for one <c>ServiceOrder</c> (<c>OrderTypeId == "ip-oficio"</c>).
/// Unlike <c>Fiscalizacao.FiscalizacaoHeader</c>/<c>Levantamento.LevantamentoHeader</c> (keyed
/// 1:1 by <c>ServiceOrderId</c> alone), Ofício has a dual identity per this ticket's own
/// description (XD01-185): a DB-generated internal <see cref="Id"/> plus a nullable,
/// Prefeitura-assigned <see cref="NumeroPrefeitura"/> — the latter is what the duplicate-upload
/// guard in <see cref="OficioCalculationService"/> keys on, not <see cref="Id"/> or
/// <see cref="ServiceOrderId"/>.
/// </summary>
public sealed class OficioHeader
{
    /// <summary>DB-generated internal identity — distinct from <see cref="ServiceOrderId"/> and
    /// from <see cref="NumeroPrefeitura"/>.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Equal to the owning <c>ServiceOrder.Id</c> — a 1:1 relationship, not a separate identity.</summary>
    public required string ServiceOrderId { get; set; }

    public required Guid MunicipioId { get; set; }

    /// <summary>Billing period this Ofício compiles interventions for, "yyyy-MM".</summary>
    public required string Competencia { get; set; }

    /// <summary>
    /// The Prefeitura-assigned document number for this Ofício, if any. Enforced unique per
    /// município by a partial index (<c>WHERE "NumeroPrefeitura" IS NOT NULL</c>) — see
    /// <c>Persistence.Configurations.OficioHeaderRecordConfiguration</c>. Cleared back to
    /// <c>null</c> by <see cref="OficioCalculationService.SupersedeAsync"/> to free the number for
    /// a corrective re-upload.
    /// </summary>
    public string? NumeroPrefeitura { get; set; }

    /// <summary>Denormalized off the owning <c>ServiceOrder.Status</c> — set <c>true</c> when the order reaches <c>Validado</c>.</summary>
    public bool IsValidated { get; set; }
}
