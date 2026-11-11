using Xunit;
using GeoAssets.Core.Isp.NeutralOperator.Leasing;
using GeoAssets.FieldService.Server;
using GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace GeoAssets.FieldService.Server.Tests;

public sealed class PixBrCodePaymentInstrumentIssuerTests
{
    [Fact]
    public async Task IssueAsync_ReturnsPixKindWithBrCodeAndRequestedIdempotencyKeyAsReference()
    {
        var now = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var options = Options.Create(new PixSettlementOptions { PixKey = "11999998888", MerchantName = "Operator", MerchantCity = "SAO PAULO" });
        var issuer = new PixBrCodePaymentInstrumentIssuer(timeProvider, options);
        var invoice = new LeaseInvoice("11.111.111/0001-11", new DateOnly(2026, 11, 1), [new LeaseInvoiceLineItem("x", 10m, LeaseInvoiceLineItemKind.FixationCharge)]);

        var result = await issuer.IssueAsync(invoice, "11.111.111/0001-11:2026-11", CancellationToken.None);

        Assert.Equal(PaymentInstrumentKind.Pix, result.Kind);
        Assert.Equal("11.111.111/0001-11:2026-11", result.ExternalReference);
        Assert.Equal(now, result.IssuedAtUtc);
        Assert.Contains("br.gov.bcb.pix", result.PresentationPayload, StringComparison.Ordinal);
    }

    [Fact]
    public void Kind_IsPix()
    {
        var issuer = new PixBrCodePaymentInstrumentIssuer(new FakeTimeProvider(), Options.Create(new PixSettlementOptions()));

        Assert.Equal(PaymentInstrumentKind.Pix, issuer.Kind);
    }
}
