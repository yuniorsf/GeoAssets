using FluentAssertions;
using GeoAssets.Workflow.Erp.Totvs;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

public class TechnicianComplianceTransitionGuardTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeComplianceRepository : ITechnicianComplianceRepository
    {
        private readonly Dictionary<string, TechnicianComplianceRecord> _records = [];

        public void Seed(TechnicianComplianceRecord record) => _records[record.TechnicianId] = record;

        public Task<TechnicianComplianceRecord?> GetAsync(string technicianId, CancellationToken ct = default) =>
            Task.FromResult(_records.GetValueOrDefault(technicianId));

        public Task UpsertAsync(TechnicianComplianceRecord record, CancellationToken ct = default)
        {
            _records[record.TechnicianId] = record;
            return Task.CompletedTask;
        }
    }

    private static TechnicianComplianceRecord CompliantRecord(string technicianId) => new(
        technicianId,
        [
            new TechnicianCertificationStatus(TechnicianCertificationType.Nr10Electrical, Now.AddDays(10)),
            new TechnicianCertificationStatus(TechnicianCertificationType.Nr35WorkAtHeight, Now.AddDays(10)),
            new TechnicianCertificationStatus(TechnicianCertificationType.AsoMedicalClearance, Now.AddDays(10)),
        ],
        [],
        Now);

    private static TechnicianComplianceRecord ExpiredRecord(string technicianId) => new(
        technicianId,
        [
            new TechnicianCertificationStatus(TechnicianCertificationType.Nr10Electrical, Now.AddDays(-1)),
            new TechnicianCertificationStatus(TechnicianCertificationType.Nr35WorkAtHeight, Now.AddDays(10)),
            new TechnicianCertificationStatus(TechnicianCertificationType.AsoMedicalClearance, Now.AddDays(10)),
        ],
        [],
        Now);

    [Fact]
    public async Task CanStartWorkAsync_NoAssignee_FailsOpen()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));
        var guard = new TechnicianComplianceTransitionGuard(new FakeComplianceRepository(), timeProvider);
        var order = new ServiceOrder { AssignedTo = null };

        (await guard.CanStartWorkAsync(order)).Should().BeTrue();
    }

    [Fact]
    public async Task CanStartWorkAsync_AssigneeNeverSynced_FailsOpen()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));
        var guard = new TechnicianComplianceTransitionGuard(new FakeComplianceRepository(), timeProvider);
        var order = new ServiceOrder { AssignedTo = "tech-unknown" };

        (await guard.CanStartWorkAsync(order)).Should().BeTrue();
    }

    [Fact]
    public async Task CanStartWorkAsync_CompliantAssignee_ReturnsTrue()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));
        var repository = new FakeComplianceRepository();
        repository.Seed(CompliantRecord("tech-1"));
        var guard = new TechnicianComplianceTransitionGuard(repository, timeProvider);
        var order = new ServiceOrder { AssignedTo = "tech-1" };

        (await guard.CanStartWorkAsync(order)).Should().BeTrue();
    }

    [Fact]
    public async Task CanStartWorkAsync_NonCompliantAssignee_ReturnsFalse()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));
        var repository = new FakeComplianceRepository();
        repository.Seed(ExpiredRecord("tech-1"));
        var guard = new TechnicianComplianceTransitionGuard(repository, timeProvider);
        var order = new ServiceOrder { AssignedTo = "tech-1" };

        (await guard.CanStartWorkAsync(order)).Should().BeFalse();
    }

    [Fact]
    public async Task CanStartWorkAsync_TechnicianBecomesNonCompliantAfterTimeAdvances_ReturnsFalse()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero));
        var repository = new FakeComplianceRepository();
        repository.Seed(new TechnicianComplianceRecord(
            "tech-1",
            [
                new TechnicianCertificationStatus(TechnicianCertificationType.Nr10Electrical, Now.AddDays(1)),
                new TechnicianCertificationStatus(TechnicianCertificationType.Nr35WorkAtHeight, Now.AddDays(10)),
                new TechnicianCertificationStatus(TechnicianCertificationType.AsoMedicalClearance, Now.AddDays(10)),
            ],
            [],
            Now));
        var guard = new TechnicianComplianceTransitionGuard(repository, timeProvider);
        var order = new ServiceOrder { AssignedTo = "tech-1" };

        (await guard.CanStartWorkAsync(order)).Should().BeTrue();

        timeProvider.Advance(TimeSpan.FromDays(2));

        (await guard.CanStartWorkAsync(order)).Should().BeFalse();
    }

    [Fact]
    public async Task CanArriveAsync_AndCanCompleteAsync_AlwaysTrue()
    {
        var guard = new TechnicianComplianceTransitionGuard(new FakeComplianceRepository(), new FakeTimeProvider(new DateTimeOffset(Now, TimeSpan.Zero)));
        var order = new ServiceOrder();

        (await guard.CanArriveAsync(order, new GeoAssets.Core.Models.Geometry.GeoPoint(0, 0))).Should().BeTrue();
        (await guard.CanCompleteAsync(order)).Should().BeTrue();
    }
}
