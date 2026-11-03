using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// Drives a neutral-operator clandestine-attachment-notice order (XD01-263:
/// <see cref="ClandestineAttachmentOrderTypes.ClandestineNoticeId"/>) through its
/// Active→CaboARevelia→Regularized lifecycle.
/// </summary>
public interface IClandestineAttachmentService
{
    /// <summary>
    /// Flags the order's attachment as undeclared, transitioning
    /// <see cref="ClandestineAttachmentWorkflowGraph.Active"/> →
    /// <see cref="ClandestineAttachmentWorkflowGraph.CaboARevelia"/> and stamping the 30-day
    /// statutory regularization deadline (from now).
    /// </summary>
    Task<ClandestineFlagResult> FlagAsync(string serviceOrderId, string? performedBy = null, CancellationToken ct = default);

    /// <summary>
    /// Transitions a flagged order to <see cref="ClandestineAttachmentWorkflowGraph.Regularized"/>.
    /// Throws <see cref="ClandestineNoticeWindowElapsedException"/> if the 30-day deadline has
    /// already passed as of now.
    /// </summary>
    Task<ClandestineFlagResult> RegularizeAsync(string serviceOrderId, string? performedBy = null, CancellationToken ct = default);

    /// <summary>
    /// All clandestine-notice orders still in <see cref="ClandestineAttachmentWorkflowGraph.CaboARevelia"/>
    /// whose 30-day deadline has already passed as of <paramref name="asOfUtc"/> — the read
    /// surface a future cleanup-drive batching ticket (XD01-264) will query. Deliberately does
    /// not itself batch or dispatch anything.
    /// </summary>
    Task<IReadOnlyList<ClandestineFlagResult>> GetElapsedUnregularizedAsync(DateTimeOffset asOfUtc, CancellationToken ct = default);

    /// <summary>
    /// Contests a flagged order by submitting evidence (e.g. a historical approval receipt
    /// reference/digest), transitioning <see cref="ClandestineAttachmentWorkflowGraph.CaboARevelia"/>
    /// → <see cref="ClandestineAttachmentWorkflowGraph.Disputed"/> — added for XD01-269's tenant
    /// self-service portal. Unlike <see cref="RegularizeAsync"/>, this does not require the
    /// statutory window to still be open: contesting that the flag was ever valid is a different
    /// claim from having since fixed it, and is reviewable regardless of elapsed time.
    /// </summary>
    Task<ClandestineFlagResult> DisputeAsync(string serviceOrderId, string evidenceReference, string? performedBy = null, CancellationToken ct = default);

    /// <summary>
    /// Resolves a dispute (XD01-270) — added for the neutral operator's mediation team.
    /// <paramref name="upheld"/> <c>true</c> transitions <see cref="ClandestineAttachmentWorkflowGraph.Disputed"/>
    /// → <see cref="ClandestineAttachmentWorkflowGraph.Regularized"/> (the tenant's contest is
    /// accepted — the attachment was legitimate all along). <c>false</c> transitions back to
    /// <see cref="ClandestineAttachmentWorkflowGraph.CaboARevelia"/>, where the original
    /// statutory deadline — stamped once at flag-time by <see cref="FlagAsync"/> and never
    /// reset — still applies; if it has already elapsed, the next cleanup-drive batching run's
    /// <see cref="GetElapsedUnregularizedAsync"/> call picks the order straight back up, which
    /// is this ticket's "back to the cleanup-drive path" resolution outcome.
    /// </summary>
    Task<ClandestineFlagResult> ResolveDisputeAsync(string serviceOrderId, bool upheld, string? performedBy = null, CancellationToken ct = default);

    /// <summary>
    /// All disputes (XD01-270) still in <see cref="ClandestineAttachmentWorkflowGraph.Disputed"/>
    /// whose mediation SLA (<see cref="ClandestineAttachmentService.DisputeMediationSla"/>,
    /// stamped at <see cref="DisputeAsync"/>-time) has already elapsed as of
    /// <paramref name="asOfUtc"/> — the read surface a neutral operator's mediation dashboard
    /// queries for overdue follow-up. Deliberately does not itself resolve or escalate anything.
    /// </summary>
    Task<IReadOnlyList<ClandestineDisputeResult>> GetOverdueDisputesAsync(DateTimeOffset asOfUtc, CancellationToken ct = default);

    /// <summary>
    /// All clandestine-notice orders currently in <see cref="ClandestineAttachmentWorkflowGraph.CaboARevelia"/>
    /// — i.e. actively flagged and not yet resolved — belonging to <paramref name="organizationId"/>.
    /// The read surface XD01-269's tenant portal uses to show a logged-in ISP "poles flagged for
    /// regularization affecting them," scoped by each order's own <c>OrganizationId</c>
    /// (<see cref="GeoAssets.Core.Interfaces.IOrgOwnedResource"/>) — the same real tenant-isolation
    /// boundary <c>OrgResourceAuthorizationHandler</c> enforces elsewhere in this codebase.
    /// </summary>
    Task<IReadOnlyList<IServiceOrder>> GetActiveFlaggedForOrganizationAsync(Guid organizationId, CancellationToken ct = default);
}
