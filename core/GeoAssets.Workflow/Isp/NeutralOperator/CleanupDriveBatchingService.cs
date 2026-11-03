using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Evidence;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// Default implementation of <see cref="ICleanupDriveBatchingService"/> (XD01-264).
///
/// <strong>Reconstruction notice — "street corridor" grouping:</strong> no street/address/
/// corridor data model exists anywhere in this codebase, and the real BDGD pole-ingestion
/// ticket (XD01-257) remains blocked on missing production data, so "batched by street
/// corridor" cannot be grounded in a real address field today. This implementation instead
/// uses single-linkage spatial clustering over each flagged pole's own point geometry
/// (<see cref="GeodeticDistanceCalculator"/>): any two poles within
/// <see cref="DefaultCorridorRadiusMeters"/> of each other join the same batch, and that
/// relationship chains transitively (pole A near B near C batches all three even if A and C
/// are themselves farther apart than the threshold) — the same "walk one corridor, batch what
/// you pass" shape a real cleanup-drive crew would want, without needing a named street.
/// 150 meters is a reasonable default for "about one urban block between utility poles,"
/// not a confirmed business rule — flagging this exactly like
/// <c>LevantamentoWorkbookRowParser</c>'s legacy-column guess and XD01-259's invented schema
/// bounds. Replace with a real corridor/address-based grouping once one exists.
/// </summary>
public sealed class CleanupDriveBatchingService(
    IClandestineAttachmentService clandestineAttachmentService,
    IServiceOrderRepository orderRepository,
    IOrderNotificationService notificationService,
    TimeProvider timeProvider) : ICleanupDriveBatchingService
{
    public const double DefaultCorridorRadiusMeters = 150.0;

    private const string AttrSourceNoticeOrderIds = "sourceClandestineNoticeOrderIds";

    private const string TransitRecipientId = "agency:municipal-transit";
    private const string UtilityRecipientId = "agency:electric-distributor";
    private const string CrewRecipientId = "agency:neutral-operator-crew";

    private sealed record Candidate(string NoticeOrderId, string PoleFeatureId, GeoPoint Point, Guid OrganizationId);

    public async Task<IReadOnlyList<CutAndRemoveBatchResult>> BatchAndDispatchExpiredNoticesAsync(
        DateTimeOffset asOfUtc, string? performedBy = null, CancellationToken ct = default)
    {
        var elapsed = await clandestineAttachmentService.GetElapsedUnregularizedAsync(asOfUtc, ct).ConfigureAwait(false);
        if (elapsed.Count == 0)
        {
            return [];
        }

        var candidates = await CollectCandidatesAsync(elapsed, ct).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return [];
        }

        var clusters = ClusterByCorridor(candidates, DefaultCorridorRadiusMeters);
        var performer = performedBy ?? "system";

        var results = new List<CutAndRemoveBatchResult>();
        foreach (var cluster in clusters)
        {
            results.Add(await CreateAndDispatchBatchAsync(cluster, performer, ct).ConfigureAwait(false));
        }

        return results;
    }

    private async Task<List<Candidate>> CollectCandidatesAsync(
        IReadOnlyList<ClandestineFlagResult> elapsed, CancellationToken ct)
    {
        var candidates = new List<Candidate>();
        foreach (var notice in elapsed)
        {
            var noticeOrder = await orderRepository.GetByIdAsync(notice.ServiceOrderId, ct).ConfigureAwait(false);
            var pole = noticeOrder?.Features.Count > 0 ? noticeOrder.Features[0] : null;
            if (pole?.Geometry is not GeoPoint point)
            {
                continue;
            }

            candidates.Add(new Candidate(notice.ServiceOrderId, pole.Id, point, noticeOrder!.OrganizationId));
        }

        return candidates;
    }

    private async Task<CutAndRemoveBatchResult> CreateAndDispatchBatchAsync(
        List<Candidate> cluster, string performedBy, CancellationToken ct)
    {
        var poleFeatureIds = cluster.Select(c => c.PoleFeatureId).Distinct(StringComparer.Ordinal).ToArray();
        var sourceNoticeIds = cluster.Select(c => c.NoticeOrderId).Distinct(StringComparer.Ordinal).ToArray();
        var now = timeProvider.GetUtcNow();

        var order = new ServiceOrder
        {
            OrderTypeId = CutAndRemoveOrderTypes.CutAndRemoveId,
            Title = $"Cut and Remove — {poleFeatureIds.Length} pole(s)",
            Status = CutAndRemoveWorkflowGraph.Open,
            CreatedBy = performedBy,
            CreatedAt = now.UtcDateTime,
            OrganizationId = cluster[0].OrganizationId,
            FeatureIds = poleFeatureIds,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [AttrSourceNoticeOrderIds] = string.Join(',', sourceNoticeIds),
            },
        };

        await orderRepository.AddAsync(order, ct).ConfigureAwait(false);
        await orderRepository.AppendActionAsync(
            order.Id,
            new OrderActionLog(
                OrderActionType.Dispatch,
                performedBy,
                now.UtcDateTime,
                $"Dispatched cleanup-drive cut order for {poleFeatureIds.Length} pole(s)",
                CutAndRemoveWorkflowGraph.Dispatched),
            ct).ConfigureAwait(false);

        await DispatchToAgenciesAsync(order.Id, performedBy, ct).ConfigureAwait(false);

        return new CutAndRemoveBatchResult(order.Id, poleFeatureIds, sourceNoticeIds, now);
    }

    public async Task CompleteCutOrderAsync(
        string serviceOrderId,
        IReadOnlyList<PoleSlotRelease> releases,
        FieldPhotoEvidenceSubmission evidence,
        string deviceSecret,
        string? performedBy = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serviceOrderId);
        ArgumentNullException.ThrowIfNull(releases);
        ArgumentNullException.ThrowIfNull(evidence);

        var order = await orderRepository.GetByIdAsync(serviceOrderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{serviceOrderId}' not found.");

        if (!string.Equals(order.Status, CutAndRemoveWorkflowGraph.Dispatched, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot complete cut order '{serviceOrderId}' — status is '{order.Status}', expected '{CutAndRemoveWorkflowGraph.Dispatched}'.");
        }

        if (FieldPhotoEvidenceIngestionVerifier.RequiresRejection(evidence, deviceSecret, evidenceMandatory: true))
        {
            throw new CutOrderMissingSealedEvidenceException(serviceOrderId);
        }

        foreach (var release in releases)
        {
            release.Matrix.TransitionStatus(release.SlotIndex, PoleAttachmentOccupantStatus.ActiveReservation);
        }

        var now = timeProvider.GetUtcNow();
        await orderRepository.AppendActionAsync(
            serviceOrderId,
            new OrderActionLog(
                OrderActionType.Complete,
                performedBy ?? "system",
                now.UtcDateTime,
                $"Completed; released {releases.Count} pole slot(s) back to ActiveReservation",
                CutAndRemoveWorkflowGraph.Completed),
            ct).ConfigureAwait(false);
    }

    private async Task DispatchToAgenciesAsync(string orderId, string performedBy, CancellationToken ct)
    {
        foreach (var (recipientId, agency) in new[]
        {
            (TransitRecipientId, "transit"),
            (UtilityRecipientId, "utility"),
            (CrewRecipientId, "crew"),
        })
        {
            await notificationService.NotifyStateChangedAsync(
                orderId,
                CutAndRemoveOrderTypes.CutAndRemoveId,
                CutAndRemoveWorkflowGraph.Open,
                CutAndRemoveWorkflowGraph.Dispatched,
                performedBy,
                [recipientId],
                metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["agency"] = agency },
                ct: ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Single-linkage clustering via union-find: O(n²) pairwise distance checks, which is fine
    /// at the scale a 30-day clandestine-notice backlog realistically produces (tens, not
    /// millions, of poles per run) — not worth a spatial index for this batch job.
    /// </summary>
    private static List<List<Candidate>> ClusterByCorridor(List<Candidate> candidates, double radiusMeters)
    {
        var parent = new int[candidates.Count];
        for (var i = 0; i < parent.Length; i++) parent[i] = i;

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        void Union(int a, int b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA != rootB) parent[rootA] = rootB;
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            for (var j = i + 1; j < candidates.Count; j++)
            {
                if (GeodeticDistanceCalculator.IsWithinDistance(candidates[i].Point, candidates[j].Point, radiusMeters))
                {
                    Union(i, j);
                }
            }
        }

        var groups = new Dictionary<int, List<Candidate>>();
        for (var i = 0; i < candidates.Count; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var list))
            {
                list = [];
                groups[root] = list;
            }
            list.Add(candidates[i]);
        }

        return groups.Values.ToList();
    }
}
