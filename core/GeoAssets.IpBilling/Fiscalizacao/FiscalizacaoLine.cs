namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// One "código de instalação" row of a Fiscalização spot-check, aggregating its
/// <see cref="OperationGroups"/> counts into the <see cref="Agregado"/>/<see cref="Retroativo"/>
/// totals via <see cref="FiscalizacaoLineCalculator.Recalculate"/>. Unlike
/// <c>Levantamento.LevantamentoTransformerLine</c>, this line is never resolved to a
/// <c>GeoFeature</c> and carries no kWh — Fiscalização's unit of record is a point-count
/// reconciliation, per this ticket's own description (XD01-182). Converting
/// <see cref="Agregado"/>/<see cref="Retroativo"/> into a billable figure is explicitly
/// deferred to the Faturamento auto-posting wiring (XD01-188) — see
/// <c>Models.Faturamento</c>'s own doc comment.
/// </summary>
public sealed class FiscalizacaoLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the owning <see cref="FiscalizacaoHeader.ServiceOrderId"/>.</summary>
    public required string ServiceOrderId { get; set; }

    /// <summary>Legacy installation code as it appears in the uploaded spreadsheet row.</summary>
    public required string CodigoInstalacao { get; set; }

    public List<FiscalizacaoOperationGroupDetail> OperationGroups { get; init; } = [];

    /// <summary>
    /// <c>Agregado₁ − Agregado₂ + Agregado₃ − Agregado₄</c> — can legitimately be negative,
    /// never clamped. Computed and persisted by <see cref="FiscalizacaoLineCalculator.Recalculate"/>.
    /// </summary>
    public int Agregado { get; set; }

    /// <summary>
    /// <c>Retroativo₁ − Retroativo₂</c> ONLY — operation groups 3/4 are deliberately excluded
    /// from this figure, a confirmed real business rule distinct from <see cref="Agregado"/>'s
    /// formula. Do not "fix" this to include groups 3/4.
    /// </summary>
    public int Retroativo { get; set; }
}
