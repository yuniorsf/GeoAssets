using GeoAssets.Core.Isp.NeutralOperator.Leasing;

namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>
/// Drives one <see cref="LeaseInvoice"/> through NF Com issuance → payment-instrument issuance →
/// ERP posting, persisting progress after each step so a retried call resumes from the first
/// incomplete step instead of re-issuing anything already done (XD01-268's "no double-invoicing"
/// AC). <see cref="ReconcileSettlementAsync"/> handles the separate paid-webhook path.
/// </summary>
public sealed class LeaseInvoiceSettlementOrchestrator
{
    private readonly INfComDocumentGenerator _nfComGenerator;
    private readonly Dictionary<PaymentInstrumentKind, IPaymentInstrumentIssuer> _paymentIssuers;
    private readonly IErpFinancialPostingClient _erpClient;
    private readonly ILeaseInvoiceSettlementRepository _repository;

    public LeaseInvoiceSettlementOrchestrator(
        INfComDocumentGenerator nfComGenerator,
        IEnumerable<IPaymentInstrumentIssuer> paymentIssuers,
        IErpFinancialPostingClient erpClient,
        ILeaseInvoiceSettlementRepository repository)
    {
        ArgumentNullException.ThrowIfNull(nfComGenerator);
        ArgumentNullException.ThrowIfNull(paymentIssuers);
        ArgumentNullException.ThrowIfNull(erpClient);
        ArgumentNullException.ThrowIfNull(repository);

        _nfComGenerator = nfComGenerator;
        _paymentIssuers = paymentIssuers.ToDictionary(issuer => issuer.Kind);
        _erpClient = erpClient;
        _repository = repository;
    }

    public async Task<LeaseInvoiceSettlementRunResult> SettleAsync(
        IReadOnlyList<LeaseInvoice> invoices,
        PaymentInstrumentKind paymentInstrumentKind,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(invoices);

        var settled = 0;
        var alreadySettledSkipped = 0;
        var failures = new List<(string TenantCnpj, string Reason)>();

        foreach (var invoice in invoices)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var wasAlreadySettled = await SettleOneAsync(invoice, paymentInstrumentKind, ct).ConfigureAwait(false);
                if (wasAlreadySettled)
                {
                    alreadySettledSkipped++;
                }
                else
                {
                    settled++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add((invoice.TenantCnpj, ex.Message));

                // Preserve whatever progress SettleOneAsync already persisted (e.g. NfComIssued)
                // instead of overwriting it with a blank record — a retry must resume from the
                // last completed step, not restart from Pending.
                var progress = await _repository.TryGetAsync(invoice.TenantCnpj, invoice.BillingPeriod, ct).ConfigureAwait(false)
                    ?? new LeaseInvoiceSettlementRecord(invoice.TenantCnpj, invoice.BillingPeriod, LeaseInvoiceSettlementStatus.Pending);

                await _repository.SaveAsync(progress with { FailureReason = ex.Message }, ct).ConfigureAwait(false);
            }
        }

        return new LeaseInvoiceSettlementRunResult(invoices.Count, settled, alreadySettledSkipped, failures);
    }

    /// <returns><c>true</c> if this invoice's period was already posted to the ERP before this call (no work done).</returns>
    private async Task<bool> SettleOneAsync(LeaseInvoice invoice, PaymentInstrumentKind paymentInstrumentKind, CancellationToken ct)
    {
        var existing = await _repository.TryGetAsync(invoice.TenantCnpj, invoice.BillingPeriod, ct).ConfigureAwait(false);
        if (existing is { Status: LeaseInvoiceSettlementStatus.PostedToErp or LeaseInvoiceSettlementStatus.Paid })
        {
            return true;
        }

        var record = existing ?? new LeaseInvoiceSettlementRecord(invoice.TenantCnpj, invoice.BillingPeriod, LeaseInvoiceSettlementStatus.Pending);

        if (record.Status == LeaseInvoiceSettlementStatus.Pending)
        {
            var nfCom = await _nfComGenerator.GenerateAsync(invoice, ct).ConfigureAwait(false);
            record = record with { Status = LeaseInvoiceSettlementStatus.NfComIssued, NfComDocumentNumber = nfCom.DocumentNumber, FailureReason = null };
            await _repository.SaveAsync(record, ct).ConfigureAwait(false);
        }

        if (record.Status == LeaseInvoiceSettlementStatus.NfComIssued)
        {
            if (!_paymentIssuers.TryGetValue(paymentInstrumentKind, out var issuer))
            {
                throw new InvalidOperationException(
                    $"No {nameof(IPaymentInstrumentIssuer)} registered for {paymentInstrumentKind}.");
            }

            var instrument = await issuer.IssueAsync(invoice, record.IdempotencyKey, ct).ConfigureAwait(false);
            record = record with { Status = LeaseInvoiceSettlementStatus.PaymentInstrumentIssued, PaymentInstrument = instrument };
            await _repository.SaveAsync(record, ct).ConfigureAwait(false);
        }

        if (record.Status == LeaseInvoiceSettlementStatus.PaymentInstrumentIssued)
        {
            var paymentInstrument = record.PaymentInstrument
                ?? throw new InvalidOperationException("Settlement record reached PaymentInstrumentIssued status without a PaymentInstrument.");
            var nfComDocumentNumber = record.NfComDocumentNumber
                ?? throw new InvalidOperationException("Settlement record reached PaymentInstrumentIssued status without an NfComDocumentNumber.");

            await _erpClient.PostAsync(
                new LeaseInvoiceErpPostingRequest(
                    invoice.TenantCnpj,
                    invoice.BillingPeriod,
                    invoice.Total,
                    nfComDocumentNumber,
                    paymentInstrument.Kind,
                    paymentInstrument.ExternalReference,
                    record.IdempotencyKey),
                ct).ConfigureAwait(false);

            record = record with { Status = LeaseInvoiceSettlementStatus.PostedToErp };
            await _repository.SaveAsync(record, ct).ConfigureAwait(false);
        }

        return false;
    }

    public async Task ReconcileSettlementAsync(SettlementWebhookPayload webhook, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(webhook);

        var record = await _repository.TryGetAsync(webhook.TenantCnpj, webhook.BillingPeriod, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No settlement record for tenant '{webhook.TenantCnpj}' period {webhook.BillingPeriod:yyyy-MM} — cannot reconcile a payment for an invoice that was never posted.");

        if (record.Status == LeaseInvoiceSettlementStatus.Paid)
        {
            return;
        }

        await _repository.SaveAsync(
            record with { Status = LeaseInvoiceSettlementStatus.Paid, PaidAtUtc = webhook.PaidAtUtc },
            ct).ConfigureAwait(false);
    }
}
