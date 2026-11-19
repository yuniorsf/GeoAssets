using System.Globalization;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>Default implementation of <see cref="IClandestineAttachmentService"/>.</summary>
public sealed class ClandestineAttachmentService(
    IServiceOrderRepository orderRepository,
    TimeProvider timeProvider) : IClandestineAttachmentService
{
    public const string AttrFlaggedAtUtc = "clandestineFlaggedAtUtc";
    public const string AttrDeadlineUtc = "clandestineRegularizationDeadlineUtc";
    public const string AttrDisputeEvidenceReference = "clandestineDisputeEvidenceReference";
    public const string AttrDisputeSubmittedAtUtc = "clandestineDisputeSubmittedAtUtc";
    public const string AttrDisputeDeadlineUtc = "clandestineDisputeMediationDeadlineUtc";

    private static readonly TimeSpan RegularizationWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// XD01-270's mediation SLA — how long the neutral operator's mediation team has to resolve
    /// a submitted dispute before it counts as overdue. No real SLA is specified anywhere in
    /// this epic for dispute mediation (unlike the clandestine notice's explicit 30-day
    /// statutory window), so 15 calendar days is an illustrative default proportionate to that
    /// window, not a confirmed business rule — flagging this exactly like
    /// <c>CleanupDriveBatchingService.DefaultCorridorRadiusMeters</c> and XD01-259's invented
    /// schema bounds. Plain calendar days, not business days — no business-day calculator
    /// exists in this codebase and building one for an already-illustrative default would be
    /// unjustified complexity.
    /// </summary>
    public static readonly TimeSpan DisputeMediationSla = TimeSpan.FromDays(15);

    public async Task<ClandestineFlagResult> FlagAsync(string serviceOrderId, string? performedBy = null, CancellationToken ct = default)
    {
        var order = await LoadMutableOrderAsync(serviceOrderId, ct).ConfigureAwait(false);
        if (!string.Equals(order.Status, ClandestineAttachmentWorkflowGraph.Active, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot flag order '{serviceOrderId}' — status is '{order.Status}', expected '{ClandestineAttachmentWorkflowGraph.Active}'.");
        }

        var now = timeProvider.GetUtcNow();
        var deadline = now + RegularizationWindow;

        order.Attributes[AttrFlaggedAtUtc] = now.ToString("O", CultureInfo.InvariantCulture);
        order.Attributes[AttrDeadlineUtc] = deadline.ToString("O", CultureInfo.InvariantCulture);
        await orderRepository.UpdateAsync(order, ct).ConfigureAwait(false);

        await orderRepository.AppendActionAsync(
            serviceOrderId,
            new OrderActionLog(
                OrderActionType.FlagClandestine,
                performedBy ?? "system",
                now.UtcDateTime,
                $"Flagged as cabo à revelia; regularization deadline {deadline:O}",
                ClandestineAttachmentWorkflowGraph.CaboARevelia),
            ct).ConfigureAwait(false);

        return new ClandestineFlagResult(serviceOrderId, ClandestineAttachmentWorkflowGraph.CaboARevelia, now, deadline);
    }

    public async Task<ClandestineFlagResult> RegularizeAsync(string serviceOrderId, string? performedBy = null, CancellationToken ct = default)
    {
        var order = await LoadMutableOrderAsync(serviceOrderId, ct).ConfigureAwait(false);
        if (!string.Equals(order.Status, ClandestineAttachmentWorkflowGraph.CaboARevelia, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot regularize order '{serviceOrderId}' — status is '{order.Status}', expected '{ClandestineAttachmentWorkflowGraph.CaboARevelia}'.");
        }

        var (flaggedAt, deadline) = ReadFlagAttributes(order);
        var now = timeProvider.GetUtcNow();
        if (now > deadline)
        {
            throw new ClandestineNoticeWindowElapsedException(serviceOrderId, deadline, now);
        }

        await orderRepository.AppendActionAsync(
            serviceOrderId,
            new OrderActionLog(
                OrderActionType.Approve,
                performedBy ?? "system",
                now.UtcDateTime,
                "Regularized within the 30-day statutory window",
                ClandestineAttachmentWorkflowGraph.Regularized),
            ct).ConfigureAwait(false);

        return new ClandestineFlagResult(serviceOrderId, ClandestineAttachmentWorkflowGraph.Regularized, flaggedAt, deadline);
    }

    public async Task<ClandestineFlagResult> DisputeAsync(string serviceOrderId, string evidenceReference, string? performedBy = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceReference);

        var order = await LoadMutableOrderAsync(serviceOrderId, ct).ConfigureAwait(false);
        if (!string.Equals(order.Status, ClandestineAttachmentWorkflowGraph.CaboARevelia, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot dispute order '{serviceOrderId}' — status is '{order.Status}', expected '{ClandestineAttachmentWorkflowGraph.CaboARevelia}'.");
        }

        var (flaggedAt, deadline) = ReadFlagAttributes(order);
        var now = timeProvider.GetUtcNow();
        var mediationDeadline = now + DisputeMediationSla;

        order.Attributes[AttrDisputeEvidenceReference] = evidenceReference;
        order.Attributes[AttrDisputeSubmittedAtUtc] = now.ToString("O", CultureInfo.InvariantCulture);
        order.Attributes[AttrDisputeDeadlineUtc] = mediationDeadline.ToString("O", CultureInfo.InvariantCulture);
        await orderRepository.UpdateAsync(order, ct).ConfigureAwait(false);

        await orderRepository.AppendActionAsync(
            serviceOrderId,
            new OrderActionLog(
                OrderActionType.Dispute,
                performedBy ?? "system",
                now.UtcDateTime,
                $"Disputed with evidence reference '{evidenceReference}'; mediation deadline {mediationDeadline:O}",
                ClandestineAttachmentWorkflowGraph.Disputed),
            ct).ConfigureAwait(false);

        return new ClandestineFlagResult(serviceOrderId, ClandestineAttachmentWorkflowGraph.Disputed, flaggedAt, deadline);
    }

    public async Task<ClandestineFlagResult> ResolveDisputeAsync(string serviceOrderId, bool upheld, string? performedBy = null, CancellationToken ct = default)
    {
        var order = await LoadMutableOrderAsync(serviceOrderId, ct).ConfigureAwait(false);
        if (!string.Equals(order.Status, ClandestineAttachmentWorkflowGraph.Disputed, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cannot resolve dispute for order '{serviceOrderId}' — status is '{order.Status}', expected '{ClandestineAttachmentWorkflowGraph.Disputed}'.");
        }

        var (flaggedAt, deadline) = ReadFlagAttributes(order);
        var now = timeProvider.GetUtcNow();

        // Upheld: the tenant's contest is accepted — the attachment was legitimate all along,
        // same terminal state a real fix reaches. Rejected: back to CaboARevelia, where the
        // original statutory deadline (stamped once at flag-time, never reset) still applies —
        // if it's already past, the next cleanup-drive batching run's GetElapsedUnregularizedAsync
        // call picks it straight back up, which is "the cleanup-drive path" this ticket's AC
        // refers to; no separate re-routing is needed since that transition already existed.
        var (action, resultingStatus, comment) = upheld
            ? (OrderActionType.Approve, ClandestineAttachmentWorkflowGraph.Regularized, "Dispute upheld — attachment regularized")
            : (OrderActionType.Reject, ClandestineAttachmentWorkflowGraph.CaboARevelia, "Dispute rejected — reverted to cabo à revelia");

        await orderRepository.AppendActionAsync(
            serviceOrderId,
            new OrderActionLog(action, performedBy ?? "system", now.UtcDateTime, comment, resultingStatus),
            ct).ConfigureAwait(false);

        return new ClandestineFlagResult(serviceOrderId, resultingStatus, flaggedAt, deadline);
    }

    public async Task<IReadOnlyList<ClandestineDisputeResult>> GetOverdueDisputesAsync(DateTimeOffset asOfUtc, CancellationToken ct = default)
    {
        var candidates = await orderRepository.GetByOrderTypeAsync(
            ClandestineAttachmentOrderTypes.ClandestineNoticeId, ct).ConfigureAwait(false);

        var results = new List<ClandestineDisputeResult>();
        foreach (var order in candidates)
        {
            if (!string.Equals(order.Status, ClandestineAttachmentWorkflowGraph.Disputed, StringComparison.Ordinal))
                continue;

            var (submittedAt, mediationDeadline) = ReadDisputeAttributes(order);
            if (asOfUtc > mediationDeadline)
            {
                results.Add(new ClandestineDisputeResult(order.Id, order.OrganizationId, submittedAt, mediationDeadline));
            }
        }

        return results;
    }

    public async Task<IReadOnlyList<IServiceOrder>> GetActiveFlaggedForOrganizationAsync(Guid organizationId, CancellationToken ct = default)
    {
        var candidates = await orderRepository.GetByOrderTypeAsync(
            ClandestineAttachmentOrderTypes.ClandestineNoticeId, ct).ConfigureAwait(false);

        return [.. candidates.Where(o =>
            o.OrganizationId == organizationId &&
            string.Equals(o.Status, ClandestineAttachmentWorkflowGraph.CaboARevelia, StringComparison.Ordinal))];
    }

    public async Task<IReadOnlyList<ClandestineFlagResult>> GetElapsedUnregularizedAsync(DateTimeOffset asOfUtc, CancellationToken ct = default)
    {
        var candidates = await orderRepository.GetByOrderTypeAsync(
            ClandestineAttachmentOrderTypes.ClandestineNoticeId, ct).ConfigureAwait(false);

        var results = new List<ClandestineFlagResult>();
        foreach (var order in candidates)
        {
            if (!string.Equals(order.Status, ClandestineAttachmentWorkflowGraph.CaboARevelia, StringComparison.Ordinal))
                continue;

            var (flaggedAt, deadline) = ReadFlagAttributes(order);
            if (asOfUtc > deadline)
            {
                results.Add(new ClandestineFlagResult(order.Id, order.Status, flaggedAt, deadline));
            }
        }

        return results;
    }

    private static (DateTimeOffset FlaggedAtUtc, DateTimeOffset DeadlineUtc) ReadFlagAttributes(IServiceOrder order)
    {
        if (!order.Attributes.TryGetValue(AttrFlaggedAtUtc, out var flaggedAtText) ||
            !DateTimeOffset.TryParse(flaggedAtText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var flaggedAt))
        {
            throw new InvalidOperationException($"Order '{order.Id}' is missing a valid '{AttrFlaggedAtUtc}' attribute.");
        }

        if (!order.Attributes.TryGetValue(AttrDeadlineUtc, out var deadlineText) ||
            !DateTimeOffset.TryParse(deadlineText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var deadline))
        {
            throw new InvalidOperationException($"Order '{order.Id}' is missing a valid '{AttrDeadlineUtc}' attribute.");
        }

        return (flaggedAt, deadline);
    }

    private static (DateTimeOffset SubmittedAtUtc, DateTimeOffset MediationDeadlineUtc) ReadDisputeAttributes(IServiceOrder order)
    {
        if (!order.Attributes.TryGetValue(AttrDisputeSubmittedAtUtc, out var submittedAtText) ||
            !DateTimeOffset.TryParse(submittedAtText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var submittedAt))
        {
            throw new InvalidOperationException($"Order '{order.Id}' is missing a valid '{AttrDisputeSubmittedAtUtc}' attribute.");
        }

        if (!order.Attributes.TryGetValue(AttrDisputeDeadlineUtc, out var deadlineText) ||
            !DateTimeOffset.TryParse(deadlineText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var deadline))
        {
            throw new InvalidOperationException($"Order '{order.Id}' is missing a valid '{AttrDisputeDeadlineUtc}' attribute.");
        }

        return (submittedAt, deadline);
    }

    private async Task<ServiceOrder> LoadMutableOrderAsync(string serviceOrderId, CancellationToken ct)
    {
        var loaded = await orderRepository.GetByIdAsync(serviceOrderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{serviceOrderId}' not found.");

        return loaded as ServiceOrder
            ?? throw new InvalidOperationException($"Expected mutable {nameof(ServiceOrder)}.");
    }
}
