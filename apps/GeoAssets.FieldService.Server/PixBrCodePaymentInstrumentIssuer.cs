using GeoAssets.Core.Isp.NeutralOperator.Leasing;
using GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;
using Microsoft.Extensions.Options;

namespace GeoAssets.FieldService.Server;

/// <summary>Issues a static PIX "Copia e Cola" BR Code for a lease invoice. See <see cref="PixBrCodeFormatter"/>.</summary>
internal sealed class PixBrCodePaymentInstrumentIssuer : IPaymentInstrumentIssuer
{
    private readonly TimeProvider _timeProvider;
    private readonly PixSettlementOptions _options;

    public PixBrCodePaymentInstrumentIssuer(TimeProvider timeProvider, IOptions<PixSettlementOptions> options)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        _timeProvider = timeProvider;
        _options = options.Value ?? throw new InvalidOperationException($"{nameof(PixSettlementOptions)} value cannot be null.");
    }

    public PaymentInstrumentKind Kind => PaymentInstrumentKind.Pix;

    public Task<PaymentInstrumentIssuanceResult> IssueAsync(LeaseInvoice invoice, string settlementIdempotencyKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentException.ThrowIfNullOrWhiteSpace(settlementIdempotencyKey);
        ct.ThrowIfCancellationRequested();

        var brCode = PixBrCodeFormatter.Build(
            _options.PixKey,
            _options.MerchantName,
            _options.MerchantCity,
            invoice.Total,
            settlementIdempotencyKey);

        return Task.FromResult(new PaymentInstrumentIssuanceResult(
            PaymentInstrumentKind.Pix,
            settlementIdempotencyKey,
            brCode,
            _timeProvider.GetUtcNow()));
    }
}
