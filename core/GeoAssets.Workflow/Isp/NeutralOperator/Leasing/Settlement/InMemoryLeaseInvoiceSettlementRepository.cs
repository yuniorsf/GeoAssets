using System.Collections.Concurrent;

namespace GeoAssets.Workflow.Isp.NeutralOperator.Leasing.Settlement;

/// <summary>
/// Process-local <see cref="ILeaseInvoiceSettlementRepository"/>, same proportionality call as
/// Totvs's <c>InMemoryTechnicianComplianceRepository</c> (XD01-253) — a real, durable store is a
/// separate infrastructure concern, not this ticket's job.
/// </summary>
public sealed class InMemoryLeaseInvoiceSettlementRepository : ILeaseInvoiceSettlementRepository
{
    private readonly ConcurrentDictionary<string, LeaseInvoiceSettlementRecord> _records = new(StringComparer.Ordinal);

    public Task<LeaseInvoiceSettlementRecord?> TryGetAsync(string tenantCnpj, DateOnly billingPeriod, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = $"{tenantCnpj}:{billingPeriod:yyyy-MM}";
        return Task.FromResult(_records.TryGetValue(key, out var record) ? record : null);
    }

    public Task SaveAsync(LeaseInvoiceSettlementRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        ct.ThrowIfCancellationRequested();
        _records[record.IdempotencyKey] = record;
        return Task.CompletedTask;
    }
}
