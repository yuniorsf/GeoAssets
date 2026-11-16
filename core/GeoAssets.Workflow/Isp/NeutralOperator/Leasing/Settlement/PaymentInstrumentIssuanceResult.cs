namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>
/// One issued settlement instrument. <paramref name="PresentationPayload"/> is the kind-specific
/// thing the tenant actually pays with — for <see cref="PaymentInstrumentKind.Pix"/> this is the
/// static "Copia e Cola" BR Code string; a future <see cref="PaymentInstrumentKind.BoletoRegistrado"/>
/// issuer would put the barcode/linha digitável here instead.
/// </summary>
public sealed record PaymentInstrumentIssuanceResult(
    PaymentInstrumentKind Kind,
    string ExternalReference,
    string PresentationPayload,
    DateTimeOffset IssuedAtUtc);
