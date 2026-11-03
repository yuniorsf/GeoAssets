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
}
