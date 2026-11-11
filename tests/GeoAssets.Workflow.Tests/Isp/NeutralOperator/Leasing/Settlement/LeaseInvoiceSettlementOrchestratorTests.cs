using Xunit;
using GeoAssets.Core.Isp.NeutralOperator.Leasing;
using GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator.Leasing.Settlement;

public sealed class LeaseInvoiceSettlementOrchestratorTests
{
    private static LeaseInvoice Invoice(string tenantCnpj = "11.111.111/0001-11", int month = 11) =>
        new(tenantCnpj, new DateOnly(2026, month, 1), [new LeaseInvoiceLineItem("Fixation points", 150m, LeaseInvoiceLineItemKind.FixationCharge)]);

    private sealed class RecordingNfComGenerator : INfComDocumentGenerator
    {
        public int CallCount { get; private set; }

        public Task<NfComDocumentResult> GenerateAsync(LeaseInvoice invoice, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new NfComDocumentResult("DOC-1", "<x/>", DateTimeOffset.UnixEpoch));
        }
    }

    private sealed class RecordingPaymentIssuer : IPaymentInstrumentIssuer
    {
        public int CallCount { get; private set; }
        public PaymentInstrumentKind Kind => PaymentInstrumentKind.Pix;

        public Task<PaymentInstrumentIssuanceResult> IssueAsync(LeaseInvoice invoice, string settlementIdempotencyKey, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(new PaymentInstrumentIssuanceResult(PaymentInstrumentKind.Pix, settlementIdempotencyKey, "00020126...", DateTimeOffset.UnixEpoch));
        }
    }

    private sealed class RecordingErpClient : IErpFinancialPostingClient
    {
        public int CallCount { get; private set; }
        public LeaseInvoiceErpPostingRequest? LastRequest { get; private set; }

        public Task PostAsync(LeaseInvoiceErpPostingRequest request, CancellationToken ct)
        {
            CallCount++;
            LastRequest = request;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SettleAsync_FirstRun_RunsAllThreeStepsAndPostsToErp()
    {
        var nfCom = new RecordingNfComGenerator();
        var pix = new RecordingPaymentIssuer();
        var erp = new RecordingErpClient();
        var repo = new InMemoryLeaseInvoiceSettlementRepository();
        var orchestrator = new LeaseInvoiceSettlementOrchestrator(nfCom, [pix], erp, repo);

        var result = await orchestrator.SettleAsync([Invoice()], PaymentInstrumentKind.Pix, CancellationToken.None);

        Assert.Equal(1, result.InvoiceCount);
        Assert.Equal(1, result.SettledCount);
        Assert.Equal(0, result.AlreadySettledSkippedCount);
        Assert.Empty(result.Failures);
        Assert.Equal(1, nfCom.CallCount);
        Assert.Equal(1, pix.CallCount);
        Assert.Equal(1, erp.CallCount);
        Assert.Equal("DOC-1", erp.LastRequest?.NfComDocumentNumber);
    }

    [Fact]
    public async Task SettleAsync_AlreadyPostedToErp_SkipsWithoutReissuing()
    {
        var nfCom = new RecordingNfComGenerator();
        var pix = new RecordingPaymentIssuer();
        var erp = new RecordingErpClient();
        var repo = new InMemoryLeaseInvoiceSettlementRepository();
        var orchestrator = new LeaseInvoiceSettlementOrchestrator(nfCom, [pix], erp, repo);
        var invoice = Invoice();

        await orchestrator.SettleAsync([invoice], PaymentInstrumentKind.Pix, CancellationToken.None);
        var secondRun = await orchestrator.SettleAsync([invoice], PaymentInstrumentKind.Pix, CancellationToken.None);

        Assert.Equal(0, secondRun.SettledCount);
        Assert.Equal(1, secondRun.AlreadySettledSkippedCount);
        Assert.Equal(1, nfCom.CallCount);
        Assert.Equal(1, pix.CallCount);
        Assert.Equal(1, erp.CallCount);
    }

    [Fact]
    public async Task SettleAsync_ErpFailsThenRetried_ResumesFromErpPostingWithoutReissuingNfComOrPayment()
    {
        var nfCom = new RecordingNfComGenerator();
        var pix = new RecordingPaymentIssuer();
        var callCount = 0;
        var erp = new ThrowingThenSucceedingErpClient(() => ++callCount == 1);
        var repo = new InMemoryLeaseInvoiceSettlementRepository();
        var orchestrator = new LeaseInvoiceSettlementOrchestrator(nfCom, [pix], erp, repo);
        var invoice = Invoice();

        var firstRun = await orchestrator.SettleAsync([invoice], PaymentInstrumentKind.Pix, CancellationToken.None);
        Assert.Single(firstRun.Failures);
        Assert.Equal(0, firstRun.SettledCount);

        var secondRun = await orchestrator.SettleAsync([invoice], PaymentInstrumentKind.Pix, CancellationToken.None);

        Assert.Equal(1, secondRun.SettledCount);
        Assert.Empty(secondRun.Failures);
        Assert.Equal(1, nfCom.CallCount);
        Assert.Equal(1, pix.CallCount);
    }

    private sealed class ThrowingThenSucceedingErpClient(Func<bool> shouldThrow) : IErpFinancialPostingClient
    {
        public Task PostAsync(LeaseInvoiceErpPostingRequest request, CancellationToken ct)
        {
            if (shouldThrow())
            {
                throw new InvalidOperationException("ERP unreachable.");
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SettleAsync_NoIssuerRegisteredForRequestedKind_RecordsFailureAndDoesNotPostToErp()
    {
        var nfCom = new RecordingNfComGenerator();
        var erp = new RecordingErpClient();
        var repo = new InMemoryLeaseInvoiceSettlementRepository();
        var orchestrator = new LeaseInvoiceSettlementOrchestrator(nfCom, [], erp, repo);

        var result = await orchestrator.SettleAsync([Invoice()], PaymentInstrumentKind.BoletoRegistrado, CancellationToken.None);

        Assert.Equal(0, result.SettledCount);
        Assert.Single(result.Failures);
        Assert.Equal(0, erp.CallCount);
    }

    [Fact]
    public async Task ReconcileSettlementAsync_PostedInvoice_MarksPaidWithWebhookTimestamp()
    {
        var nfCom = new RecordingNfComGenerator();
        var pix = new RecordingPaymentIssuer();
        var erp = new RecordingErpClient();
        var repo = new InMemoryLeaseInvoiceSettlementRepository();
        var orchestrator = new LeaseInvoiceSettlementOrchestrator(nfCom, [pix], erp, repo);
        var invoice = Invoice();
        await orchestrator.SettleAsync([invoice], PaymentInstrumentKind.Pix, CancellationToken.None);

        var paidAt = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        await orchestrator.ReconcileSettlementAsync(
            new SettlementWebhookPayload(invoice.TenantCnpj, invoice.BillingPeriod, paidAt, "EXT-REF-1"),
            CancellationToken.None);

        var record = await repo.TryGetAsync(invoice.TenantCnpj, invoice.BillingPeriod, CancellationToken.None);
        Assert.Equal(LeaseInvoiceSettlementStatus.Paid, record?.Status);
        Assert.Equal(paidAt, record?.PaidAtUtc);
    }

    [Fact]
    public Task ReconcileSettlementAsync_UnknownInvoice_Throws()
    {
        var orchestrator = new LeaseInvoiceSettlementOrchestrator(
            new RecordingNfComGenerator(), [new RecordingPaymentIssuer()], new RecordingErpClient(), new InMemoryLeaseInvoiceSettlementRepository());

        return Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.ReconcileSettlementAsync(
            new SettlementWebhookPayload("99.999.999/0001-99", new DateOnly(2026, 11, 1), DateTimeOffset.UnixEpoch, "EXT-REF-2"),
            CancellationToken.None));
    }

    [Fact]
    public async Task ReconcileSettlementAsync_AlreadyPaid_IsIdempotent()
    {
        var nfCom = new RecordingNfComGenerator();
        var pix = new RecordingPaymentIssuer();
        var erp = new RecordingErpClient();
        var repo = new InMemoryLeaseInvoiceSettlementRepository();
        var orchestrator = new LeaseInvoiceSettlementOrchestrator(nfCom, [pix], erp, repo);
        var invoice = Invoice();
        await orchestrator.SettleAsync([invoice], PaymentInstrumentKind.Pix, CancellationToken.None);
        var firstPaidAt = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var webhook = new SettlementWebhookPayload(invoice.TenantCnpj, invoice.BillingPeriod, firstPaidAt, "EXT-REF-1");
        await orchestrator.ReconcileSettlementAsync(webhook, CancellationToken.None);

        var secondWebhook = webhook with { PaidAtUtc = firstPaidAt.AddDays(1) };
        await orchestrator.ReconcileSettlementAsync(secondWebhook, CancellationToken.None);

        var record = await repo.TryGetAsync(invoice.TenantCnpj, invoice.BillingPeriod, CancellationToken.None);
        Assert.Equal(firstPaidAt, record?.PaidAtUtc);
    }
}
