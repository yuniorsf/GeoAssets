using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// Optional lamp detail for some of the points one <see cref="FiscalizacaoLine"/> reports under an
/// operation group (XD01-301) — mirrors <c>Levantamento.LevantamentoLampDetail</c>, plus the
/// operation group the lamps belong to. Its <see cref="Quantity"/> is a <em>breakdown</em> of the
/// line's <see cref="FiscalizacaoOperationGroupDetail.Count"/> for the same group, never an addition to
/// it: points covered by a lamp detail convert to kWh exactly, the remainder provisionally.
/// </summary>
public sealed class FiscalizacaoLampDetail
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required Guid LineId { get; set; }

    public required FiscalizacaoOperationGroup OperationGroup { get; set; }

    public required LampTypeCode Code { get; set; }

    /// <summary>Nominal lamp wattage, in watts. Always positive.</summary>
    public required int WattageW { get; set; }

    /// <summary>Number of points of this lamp type/wattage. Always positive.</summary>
    public required int Quantity { get; set; }
}
