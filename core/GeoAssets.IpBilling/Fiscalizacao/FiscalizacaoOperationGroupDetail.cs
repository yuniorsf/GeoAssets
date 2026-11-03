namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// One operation-group count row under a <see cref="FiscalizacaoLine"/> — plain relational
/// data, mirroring <c>Levantamento.LevantamentoLampDetail</c>'s "aggregate count, never a
/// <c>GeoFeature</c>" shape.
/// </summary>
public sealed class FiscalizacaoOperationGroupDetail
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required Guid LineId { get; set; }

    public required FiscalizacaoOperationGroup OperationGroup { get; set; }

    public required int Count { get; set; }
}
