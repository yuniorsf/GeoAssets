using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Evidence;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Tests.Orders;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator;

public class CleanupDriveBatchingServiceTests
{
    private const string DeviceSecret = "test-device-secret";

    private sealed class FakeClandestineAttachmentService : IClandestineAttachmentService
    {
        public List<ClandestineFlagResult> ElapsedResults { get; } = [];

        public Task<ClandestineFlagResult> FlagAsync(string serviceOrderId, string? performedBy = null, CancellationToken ct = default) =>
            throw new NotSupportedException("Not needed by these tests.");

        public Task<ClandestineFlagResult> RegularizeAsync(string serviceOrderId, string? performedBy = null, CancellationToken ct = default) =>
            throw new NotSupportedException("Not needed by these tests.");

        public Task<IReadOnlyList<ClandestineFlagResult>> GetElapsedUnregularizedAsync(DateTimeOffset asOfUtc, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ClandestineFlagResult>>(ElapsedResults);

        public Task<ClandestineFlagResult> DisputeAsync(string serviceOrderId, string evidenceReference, string? performedBy = null, CancellationToken ct = default) =>
            throw new NotSupportedException("Not needed by these tests.");

        public Task<IReadOnlyList<IServiceOrder>> GetActiveFlaggedForOrganizationAsync(Guid organizationId, CancellationToken ct = default) =>
            throw new NotSupportedException("Not needed by these tests.");

        public Task<ClandestineFlagResult> ResolveDisputeAsync(string serviceOrderId, bool upheld, string? performedBy = null, CancellationToken ct = default) =>
            throw new NotSupportedException("Not needed by these tests.");

        public Task<IReadOnlyList<ClandestineDisputeResult>> GetOverdueDisputesAsync(DateTimeOffset asOfUtc, CancellationToken ct = default) =>
            throw new NotSupportedException("Not needed by these tests.");
    }

    private sealed record CapturedNotification(string OrderId, IReadOnlyList<string> RecipientIds, IReadOnlyDictionary<string, string>? Metadata);

    private sealed class FakeOrderNotificationService : IOrderNotificationService
    {
        public List<CapturedNotification> Calls { get; } = [];

        public Task NotifyStateChangedAsync(
            string orderId, string orderTypeId, string previous, string newStatus, string performedBy,
            IEnumerable<string> recipientIds, string? correlationId = null,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
        {
            Calls.Add(new CapturedNotification(orderId, recipientIds.ToList(), metadata));
            return Task.CompletedTask;
        }
    }

    private static ServiceOrder NewClandestineOrder(string id, GeoFeature pole, Guid orgId) => new()
    {
        Id = id,
        OrderTypeId = ClandestineAttachmentOrderTypes.ClandestineNoticeId,
        Status = ClandestineAttachmentWorkflowGraph.CaboARevelia,
        OrganizationId = orgId,
        Features = [pole],
    };

    private static GeoFeature Pole(string id, double lon, double lat) =>
        new() { Id = id, Geometry = new GeoPoint(lon, lat) };

    [Fact]
    public async Task BatchAndDispatchExpiredNoticesAsync_GroupsNearbyPoles_AndSeparatesFarOnes()
    {
        var orgId = Guid.NewGuid();
        var poleA = Pole("pole-a", -43.2000, -22.9000);
        var poleB = Pole("pole-b", -43.2000, -22.9005); // ~55m from A — same corridor
        var poleC = Pole("pole-c", -43.2000, -22.9100); // ~1.1km from A/B — separate corridor

        var repo = new FakeServiceOrderRepository();
        await repo.AddAsync(NewClandestineOrder("notice-a", poleA, orgId));
        await repo.AddAsync(NewClandestineOrder("notice-b", poleB, orgId));
        await repo.AddAsync(NewClandestineOrder("notice-c", poleC, orgId));

        var clandestine = new FakeClandestineAttachmentService();
        clandestine.ElapsedResults.AddRange([
            new ClandestineFlagResult("notice-a", ClandestineAttachmentWorkflowGraph.CaboARevelia, default, default),
            new ClandestineFlagResult("notice-b", ClandestineAttachmentWorkflowGraph.CaboARevelia, default, default),
            new ClandestineFlagResult("notice-c", ClandestineAttachmentWorkflowGraph.CaboARevelia, default, default),
        ]);

        var notifications = new FakeOrderNotificationService();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new CleanupDriveBatchingService(clandestine, repo, notifications, time);

        var batches = await service.BatchAndDispatchExpiredNoticesAsync(time.GetUtcNow(), "crew-lead-1");

        batches.Should().HaveCount(2, "A+B form one corridor batch and C forms its own");
        var abBatch = batches.Should().ContainSingle(b => b.PoleFeatureIds.Contains("pole-a")).Subject;
        abBatch.PoleFeatureIds.Should().BeEquivalentTo(["pole-a", "pole-b"]);
        abBatch.SourceClandestineNoticeOrderIds.Should().BeEquivalentTo(["notice-a", "notice-b"]);

        var cBatch = batches.Should().ContainSingle(b => b.PoleFeatureIds.Contains("pole-c")).Subject;
        cBatch.PoleFeatureIds.Should().BeEquivalentTo(["pole-c"]);

        var abOrder = await repo.GetByIdAsync(abBatch.ServiceOrderId);
        abOrder!.Status.Should().Be(CutAndRemoveWorkflowGraph.Dispatched);
        abOrder.OrderTypeId.Should().Be(CutAndRemoveOrderTypes.CutAndRemoveId);
    }

    [Fact]
    public async Task BatchAndDispatchExpiredNoticesAsync_FiresThreeDistinctAgencyNotificationsPerBatch()
    {
        var orgId = Guid.NewGuid();
        var pole = Pole("pole-1", -43.2, -22.9);

        var repo = new FakeServiceOrderRepository();
        await repo.AddAsync(NewClandestineOrder("notice-1", pole, orgId));

        var clandestine = new FakeClandestineAttachmentService();
        clandestine.ElapsedResults.Add(new ClandestineFlagResult("notice-1", ClandestineAttachmentWorkflowGraph.CaboARevelia, default, default));

        var notifications = new FakeOrderNotificationService();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new CleanupDriveBatchingService(clandestine, repo, notifications, time);

        var batches = await service.BatchAndDispatchExpiredNoticesAsync(time.GetUtcNow());

        var orderId = batches.Single().ServiceOrderId;
        notifications.Calls.Should().HaveCount(3);
        notifications.Calls.Should().OnlyContain(c => c.OrderId == orderId);

        var agencies = notifications.Calls
            .Select(c => c.Metadata!["agency"])
            .ToList();
        agencies.Should().BeEquivalentTo(["transit", "utility", "crew"]);

        var recipientSets = notifications.Calls.Select(c => c.RecipientIds.Single()).ToList();
        recipientSets.Distinct(StringComparer.Ordinal).Should().HaveCount(3, "each agency gets a distinct recipient");
    }

    [Fact]
    public async Task CompleteCutOrderAsync_WithValidSealedEvidence_ReleasesSlotAndCompletesOrder()
    {
        var repo = new FakeServiceOrderRepository();
        var dispatchedOrder = new ServiceOrder
        {
            Id = "cut-order-1",
            OrderTypeId = CutAndRemoveOrderTypes.CutAndRemoveId,
            Status = CutAndRemoveWorkflowGraph.Dispatched,
            FeatureIds = ["pole-1"],
        };
        await repo.AddAsync(dispatchedOrder);

        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };
        matrix.AddSlot(new PoleAttachmentSlot { SlotIndex = 1, Status = PoleAttachmentOccupantStatus.ClandestineUnauthorized });

        var notifications = new FakeOrderNotificationService();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new CleanupDriveBatchingService(new FakeClandestineAttachmentService(), repo, notifications, time);

        var metadata = new FieldPhotoEvidenceMetadata("inspector-1", "pole-1", time.GetUtcNow(), -22.9, -43.2, 180, dispatchedOrder.Id);
        var imageBytes = new byte[] { 1, 2, 3, 4 };
        var digest = FieldPhotoEvidenceSealer.ComputeDigest(imageBytes, metadata, DeviceSecret);
        var evidence = new FieldPhotoEvidenceSubmission(imageBytes, metadata, digest);

        await service.CompleteCutOrderAsync(
            dispatchedOrder.Id,
            [new PoleSlotRelease("pole-1", matrix, 1)],
            evidence,
            DeviceSecret,
            "crew-1");

        matrix.Slots.Single().Status.Should().Be(PoleAttachmentOccupantStatus.ActiveReservation, "the slot is freed, not deleted");

        var reloaded = await repo.GetByIdAsync(dispatchedOrder.Id);
        reloaded!.Status.Should().Be(CutAndRemoveWorkflowGraph.Completed);
    }

    [Fact]
    public async Task CompleteCutOrderAsync_WithoutSealedEvidence_ThrowsAndDoesNotReleaseOrComplete()
    {
        var repo = new FakeServiceOrderRepository();
        var dispatchedOrder = new ServiceOrder
        {
            Id = "cut-order-2",
            OrderTypeId = CutAndRemoveOrderTypes.CutAndRemoveId,
            Status = CutAndRemoveWorkflowGraph.Dispatched,
            FeatureIds = ["pole-2"],
        };
        await repo.AddAsync(dispatchedOrder);

        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-2" };
        matrix.AddSlot(new PoleAttachmentSlot { SlotIndex = 1, Status = PoleAttachmentOccupantStatus.ClandestineUnauthorized });

        var metadata = new FieldPhotoEvidenceMetadata("inspector-1", "pole-2", new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero), -22.9, -43.2, 180, dispatchedOrder.Id);
        var evidenceMissingDigest = new FieldPhotoEvidenceSubmission([1, 2, 3], metadata, ClaimedDigestBase64: null);

        var notifications = new FakeOrderNotificationService();
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new CleanupDriveBatchingService(new FakeClandestineAttachmentService(), repo, notifications, time);

        var act = () => service.CompleteCutOrderAsync(
            dispatchedOrder.Id,
            [new PoleSlotRelease("pole-2", matrix, 1)],
            evidenceMissingDigest,
            DeviceSecret,
            "crew-1");

        await act.Should().ThrowAsync<CutOrderMissingSealedEvidenceException>();

        matrix.Slots.Single().Status.Should().Be(PoleAttachmentOccupantStatus.ClandestineUnauthorized, "not released without valid evidence");
        var reloaded = await repo.GetByIdAsync(dispatchedOrder.Id);
        reloaded!.Status.Should().Be(CutAndRemoveWorkflowGraph.Dispatched, "not completed without valid evidence");
    }
}
