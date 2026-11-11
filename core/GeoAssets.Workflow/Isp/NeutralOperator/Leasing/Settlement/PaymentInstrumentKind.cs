namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>
/// Which settlement instrument an invoice is paid through (XD01-268's AC: "either a Boleto
/// or PIX QR Code"). Only <see cref="Pix"/> has a shipped issuer in this pass — see
/// <c>IPixPaymentInstrumentIssuer</c>'s remarks. <see cref="BoletoRegistrado"/> is modeled now
/// so the orchestrator's switch is exhaustive and callers can select it, but resolving an
/// <c>IPaymentInstrumentIssuer</c> for it with no registered implementation fails fast at DI
/// composition — deliberately, rather than shipping a stub that throws at request time.
/// </summary>
public enum PaymentInstrumentKind
{
    Pix = 0,
    BoletoRegistrado = 1,
}
