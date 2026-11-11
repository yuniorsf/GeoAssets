using FluentAssertions;
using GeoAssets.Workflow.Erp.Totvs;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Erp.Totvs;

public class TotvsNightlySyncOrchestratorTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FakeSyncClient : ITotvsProtheusSyncClient
    {
        public IReadOnlyList<TechnicianComplianceRecord> ComplianceToReturn { get; set; } = [];
        public IReadOnlyList<WarehouseStockIssuanceRecord> IssuancesToReturn { get; set; } = [];
        public Exception? ComplianceFailure { get; set; }
        public Exception? PayrollFailure { get; set; }
        public Exception? StockFailure { get; set; }
        public List<ProtheusPayrollExportLine> PushedLines { get; } = [];

        public Task<IReadOnlyList<TechnicianComplianceRecord>> PullTechnicianComplianceAsync(CancellationToken ct = default) =>
            ComplianceFailure is not null ? Task.FromException<IReadOnlyList<TechnicianComplianceRecord>>(ComplianceFailure) : Task.FromResult(ComplianceToReturn);

        public Task PushPayrollExportAsync(IReadOnlyList<ProtheusPayrollExportLine> lines, CancellationToken ct = default)
        {
            if (PayrollFailure is not null) return Task.FromException(PayrollFailure);
            PushedLines.AddRange(lines);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<WarehouseStockIssuanceRecord>> PullWarehouseStockIssuancesAsync(DateTime sinceUtc, CancellationToken ct = default) =>
            StockFailure is not null ? Task.FromException<IReadOnlyList<WarehouseStockIssuanceRecord>>(StockFailure) : Task.FromResult(IssuancesToReturn);
    }

    private sealed class FakeComplianceRepository : ITechnicianComplianceRepository
    {
        public List<TechnicianComplianceRecord> Upserted { get; } = [];
        public Task<TechnicianComplianceRecord?> GetAsync(string technicianId, CancellationToken ct = default) => Task.FromResult<TechnicianComplianceRecord?>(null);
        public Task UpsertAsync(TechnicianComplianceRecord record, CancellationToken ct = default) { Upserted.Add(record); return Task.CompletedTask; }
    }

    private sealed class FakePostingSource(IReadOnlyList<ApprovedUsPosting> postings) : IApprovedUsPostingSource
    {
        public Task<IReadOnlyList<ApprovedUsPosting>> GetApprovedPostingsSinceAsync(DateTime sinceUtc, CancellationToken ct = default) => Task.FromResult(postings);
    }

    private sealed class FakeConsumptionSource(IReadOnlyList<FieldMaterialConsumptionRecord> consumption) : IFieldMaterialConsumptionSource
    {
        public Task<IReadOnlyList<FieldMaterialConsumptionRecord>> GetConsumptionSinceAsync(DateTime sinceUtc, CancellationToken ct = default) => Task.FromResult(consumption);
    }

    private static TotvsSyncRetryOptions FastRetryOptions() => new() { BaseBackoffInterval = TimeSpan.FromMilliseconds(1), MaxBackoffInterval = TimeSpan.FromMilliseconds(1), MaxAttempts = 1 };

    [Fact]
    public async Task RunAsync_AllOperationsSucceed_ReturnsCountsAndNoFailures()
    {
        var syncClient = new FakeSyncClient
        {
            ComplianceToReturn = [new TechnicianComplianceRecord("tech-1", [], [], Now)],
            IssuancesToReturn = [new WarehouseStockIssuanceRecord("cable-1", "wh-1", 100m, "order-1", Now)],
        };
        var complianceRepo = new FakeComplianceRepository();
        var postingSource = new FakePostingSource([new ApprovedUsPosting("order-1", "tech-1", "us-1", 1m, 10m, Now)]);
        var consumptionSource = new FakeConsumptionSource([new FieldMaterialConsumptionRecord("cable-1", "order-1", 100m)]);
        var orchestrator = new TotvsNightlySyncOrchestrator(syncClient, complianceRepo, postingSource, consumptionSource, FastRetryOptions(), new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)));

        var result = await orchestrator.RunAsync(Now.AddDays(-1));

        result.AllOperationsSucceeded.Should().BeTrue();
        result.TechnicianComplianceRecordsSynced.Should().Be(1);
        result.PayrollExportLinesPushed.Should().Be(1);
        result.ShrinkageFlags.Should().BeEmpty();
        complianceRepo.Upserted.Should().ContainSingle(r => r.TechnicianId == "tech-1");
        syncClient.PushedLines.Should().ContainSingle(l => l.TechnicianId == "tech-1");
    }

    [Fact]
    public async Task RunAsync_ComplianceFailure_DoesNotBlockOtherTwoOperations()
    {
        var syncClient = new FakeSyncClient { ComplianceFailure = new InvalidOperationException("totvs down") };
        var orchestrator = new TotvsNightlySyncOrchestrator(
            syncClient, new FakeComplianceRepository(),
            new FakePostingSource([new ApprovedUsPosting("order-1", "tech-1", "us-1", 1m, 10m, Now)]),
            new FakeConsumptionSource([]), FastRetryOptions(), new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)));

        var result = await orchestrator.RunAsync(Now.AddDays(-1));

        result.AllOperationsSucceeded.Should().BeFalse();
        result.TechnicianComplianceFailure.Should().NotBeNull();
        result.PayrollExportFailure.Should().BeNull();
        result.StockReconciliationFailure.Should().BeNull();
        result.PayrollExportLinesPushed.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_PayrollPushFailure_IsSurfacedNotSwallowed()
    {
        var syncClient = new FakeSyncClient { PayrollFailure = new InvalidOperationException("payroll rejected") };
        var orchestrator = new TotvsNightlySyncOrchestrator(
            syncClient, new FakeComplianceRepository(),
            new FakePostingSource([new ApprovedUsPosting("order-1", "tech-1", "us-1", 1m, 10m, Now)]),
            new FakeConsumptionSource([]), FastRetryOptions(), new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)));

        var result = await orchestrator.RunAsync(Now.AddDays(-1));

        result.PayrollExportFailure.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("payroll rejected");
    }

    [Fact]
    public async Task RunAsync_StockPullFailure_IsSurfacedWithEmptyFlags()
    {
        var syncClient = new FakeSyncClient { StockFailure = new InvalidOperationException("stock feed down") };
        var orchestrator = new TotvsNightlySyncOrchestrator(
            syncClient, new FakeComplianceRepository(), new FakePostingSource([]), new FakeConsumptionSource([]), FastRetryOptions(), new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)));

        var result = await orchestrator.RunAsync(Now.AddDays(-1));

        result.StockReconciliationFailure.Should().NotBeNull();
        result.ShrinkageFlags.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_ShrinkageDetected_IsReturnedInResult()
    {
        var syncClient = new FakeSyncClient
        {
            IssuancesToReturn = [new WarehouseStockIssuanceRecord("cable-1", "wh-1", 100m, "order-1", Now)],
        };
        var orchestrator = new TotvsNightlySyncOrchestrator(
            syncClient, new FakeComplianceRepository(), new FakePostingSource([]),
            new FakeConsumptionSource([new FieldMaterialConsumptionRecord("cable-1", "order-1", 10m)]), FastRetryOptions(), new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)));

        var result = await orchestrator.RunAsync(Now.AddDays(-1));

        result.ShrinkageFlags.Should().ContainSingle(f => f.MaterialCode == "cable-1");
    }
}
