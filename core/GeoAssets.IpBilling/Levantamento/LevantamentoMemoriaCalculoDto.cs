namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// Client-side mirror of <c>IpBillingLevantamentoRestApiExtensions.LevantamentoMemoriaCalculoResponse</c>
/// — that record is <c>internal</c> to <c>GeoAssets.IpBilling.Server</c>, so this is a separate
/// (but field-for-field identical) type for JSON deserialization on the client side.
/// </summary>
public sealed record LevantamentoMemoriaCalculoDto(
    LevantamentoHeader Header,
    string OrderStatus,
    IReadOnlyList<LevantamentoTransformerLine> TransformerLines,
    IReadOnlyList<Levantamento24hLine> Lines24h,
    decimal NormalKwh,
    decimal RetroativoKwh,
    decimal Irregular24hKwh,
    decimal TotalCalculatedKwh);
