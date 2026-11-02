using GeoAssets.IpBilling.Levantamento;

namespace GeoAssets.IpBilling.Persistence.Entities;

/// <summary>EF entity for the <c>LevantamentoTransformerLines</c> table.</summary>
internal sealed class LevantamentoTransformerLineRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ServiceOrderId { get; set; } = string.Empty;

    public string CodigoInstalacao { get; set; } = string.Empty;

    public Guid? TransformerFeatureId { get; set; }

    public bool IsNewTransformer { get; set; }

    public TipoRetroacao? Tipo { get; set; }

    public decimal NormalKwh { get; set; }

    public decimal? RetroativoKwh { get; set; }

    public List<LevantamentoLampDetailRecord> LampDetails { get; init; } = [];
}
