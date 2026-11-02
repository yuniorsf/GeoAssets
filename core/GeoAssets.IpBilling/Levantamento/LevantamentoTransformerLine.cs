namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// One transformer row of a Levantamento survey, keyed by the legacy "código de instalação" as
/// it appears in the uploaded spreadsheet, resolved (via <c>ITransformerResolver</c>) to the
/// real <c>GeoFeature</c> standing in for that transformer on the map where possible.
///
/// <see cref="IsNewTransformer"/>/<see cref="Tipo"/>/<see cref="RetroativoKwh"/> are only ever
/// populated for transformers detected as new since the município's most recent validated
/// Levantamento — see <see cref="Calculation.LevantamentoRetroactiveCalculator"/>. For an
/// existing transformer, this ticket's own description keeps the retroactive figure a
/// <strong>display-time-only</strong> comparison residual against the ledger — never persisted
/// here (<see cref="Tipo"/>/<see cref="RetroativoKwh"/> both stay <c>null</c>).
/// </summary>
public sealed class LevantamentoTransformerLine
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the owning <see cref="LevantamentoHeader.ServiceOrderId"/>.</summary>
    public required string ServiceOrderId { get; set; }

    /// <summary>Legacy installation code as it appears in the uploaded spreadsheet row.</summary>
    public required string CodigoInstalacao { get; set; }

    /// <summary>The resolved "Transformador (IP)" <c>GeoFeature.Id</c>, or <c>null</c> when
    /// <c>ITransformerResolver</c> found no matching <c>IpTransformerIndex</c> row.</summary>
    public Guid? TransformerFeatureId { get; set; }

    public bool IsNewTransformer { get; set; }

    /// <summary>Only set when <see cref="IsNewTransformer"/> is <c>true</c>.</summary>
    public TipoRetroacao? Tipo { get; set; }

    /// <summary>Normal-lamp kWh for this transformer at the 11h52 daily rate, for the
    /// competência itself (never retroactive).</summary>
    public decimal NormalKwh { get; set; }

    /// <summary>Retroactive kWh charge, only set/persisted when <see cref="IsNewTransformer"/>
    /// is <c>true</c> — <c>null</c> for an existing transformer.</summary>
    public decimal? RetroativoKwh { get; set; }

    public List<LevantamentoLampDetail> LampDetails { get; init; } = [];
}
