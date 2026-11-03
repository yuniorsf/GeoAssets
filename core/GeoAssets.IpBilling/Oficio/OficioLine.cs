namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// One "código de instalação" row of an Ofício, aggregating its <see cref="OperationGroups"/>
/// counts into the <see cref="Agregado"/>/<see cref="Retroativo"/> totals via
/// <see cref="OficioLineCalculator.Recalculate"/>. Point-count reconciliation, same unit of
/// record as <c>Fiscalizacao.FiscalizacaoLine</c> — no kWh, no <c>GeoFeature</c> resolution.
/// </summary>
public sealed class OficioLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the owning <see cref="OficioHeader.ServiceOrderId"/>.</summary>
    public required string ServiceOrderId { get; set; }

    /// <summary>Legacy installation code as it appears in the uploaded spreadsheet row.</summary>
    public required string CodigoInstalacao { get; set; }

    public List<OficioOperationGroupDetail> OperationGroups { get; init; } = [];

    /// <summary>
    /// <c>Agregado₁ − Agregado₂ + Agregado₃ − Agregado₄</c> — can legitimately be negative,
    /// never clamped. Computed and persisted by <see cref="OficioLineCalculator.Recalculate"/>.
    /// </summary>
    public int Agregado { get; set; }

    /// <summary>
    /// Computed by <see cref="OficioLineCalculator.Recalculate"/> under whichever
    /// <see cref="OficioRetroativoPolicy"/> was in effect — see that type's doc comment for the
    /// open business-rule question this ticket (XD01-185) flags rather than silently resolves.
    /// </summary>
    public int Retroativo { get; set; }
}
