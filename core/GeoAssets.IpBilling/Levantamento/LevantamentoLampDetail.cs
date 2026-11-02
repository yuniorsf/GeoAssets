using GeoAssets.IpBilling.Models;

namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// One aggregate lamp-type/wattage count row under a <see cref="LevantamentoTransformerLine"/> —
/// plain relational data, never a <c>GeoFeature</c>, since the source spreadsheet data is
/// always an aggregate count per the parent epic's architecture decision (XD01-175).
/// </summary>
public sealed class LevantamentoLampDetail
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required Guid TransformerLineId { get; set; }

    public required LampTypeCode Code { get; set; }

    public required int WattageW { get; set; }

    public required int Quantity { get; set; }
}
