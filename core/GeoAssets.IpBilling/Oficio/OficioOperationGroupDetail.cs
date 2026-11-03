namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// One operation-group count row under an <see cref="OficioLine"/> — plain relational data,
/// mirroring <c>Fiscalizacao.FiscalizacaoOperationGroupDetail</c>'s shape.
/// </summary>
public sealed class OficioOperationGroupDetail
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required Guid LineId { get; set; }

    public required OficioOperationGroup OperationGroup { get; set; }

    public required int Count { get; set; }
}
