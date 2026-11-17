using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>
/// The preview-then-apply conversation of one planned selection with one order (XD01-312), with
/// every decision of the preview panel: what is shown, when applying is allowed, and what a
/// failure means. The panel component only renders it. Not thread-safe; used from the renderer's
/// synchronization context.
/// </summary>
public sealed class OrderTargetSelectionSession
{
    /// <summary>Rows per page of the previewed targets.</summary>
    public const int PreviewPageSize = 10;

    private readonly IOrderTargetSelectionService _service;
    private readonly string _orderId;
    private readonly OrderTargetSelectionPlan _plan;
    private readonly WorkflowPrincipal _principal;
    private int _previewSequence;

    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="orderId"/> is blank.</exception>
    public OrderTargetSelectionSession(IOrderTargetSelectionService service, string orderId, OrderTargetSelectionPlan plan, WorkflowPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(principal);

        _service = service;
        _orderId = orderId;
        _plan = plan;
        _principal = principal;
    }

    /// <summary>The last preview that arrived; null until one does.</summary>
    public SelectionPreviewSummary? Summary { get; private set; }

    /// <summary>The 1-based page of previewed targets <see cref="Summary"/> shows.</summary>
    public int PreviewPage { get; private set; } = 1;

    public bool IsPreviewing { get; private set; }

    public bool IsApplying { get; private set; }

    public bool IsBusy => IsPreviewing || IsApplying;

    /// <summary>Localization key of the last failure; cleared by the next preview or apply.</summary>
    public string? ErrorKey { get; private set; }

    /// <summary>
    /// Whether the user accepted that the selection limit leaves features out. Reset whenever a
    /// preview reports different numbers, because the acceptance was for the numbers on screen.
    /// </summary>
    public bool TruncationAccepted { get; private set; }

    public bool CanApply => !IsBusy && Summary is { } summary && summary.CanApply(TruncationAccepted);

    public void SetTruncationAccepted(bool accepted) => TruncationAccepted = accepted;

    /// <summary>
    /// Previews <paramref name="page"/> of the targets the selection would add. A preview that a
    /// newer one overtook is dropped; a failure keeps the previous summary and sets
    /// <see cref="ErrorKey"/>. Does nothing while applying.
    /// </summary>
    public async Task PreviewAsync(int page, CancellationToken ct)
    {
        if (IsApplying) return;

        var sequence = ++_previewSequence;
        (IsPreviewing, ErrorKey) = (true, null);
        try
        {
            var preview = await _service.PreviewAsync(BuildRequest(Math.Max(page, 1), acceptTruncation: false), _principal, ct);
            if (sequence != _previewSequence) return;

            Accept(SelectionPreviewSummary.From(preview), Math.Max(page, 1));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The panel went away while previewing.
        }
        catch (Exception ex)
        {
            if (sequence == _previewSequence) ErrorKey = OrderTargetMessages.SelectionErrorKey(ex);
        }
        finally
        {
            if (sequence == _previewSequence) IsPreviewing = false;
        }
    }

    /// <summary>
    /// Applies the selection when <see cref="CanApply"/>; returns the result, or null when nothing
    /// was applied (not allowed, already busy, or failed — then <see cref="ErrorKey"/> says why).
    /// When the order type's limit changed since the preview, the preview is refreshed so the user
    /// decides again on the new numbers.
    /// </summary>
    public async Task<TargetSelectionResult?> ApplyAsync(CancellationToken ct)
    {
        if (!CanApply) return null;

        var acceptTruncation = Summary!.IsTruncated && TruncationAccepted;
        (IsApplying, ErrorKey) = (true, null);

        TargetSelectionResult? result = null;
        Exception? failure = null;
        try
        {
            result = await _service.ApplyAsync(BuildRequest(1, acceptTruncation), _principal, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The panel went away while applying.
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        IsApplying = false;
        if (failure is not null)
        {
            ErrorKey = OrderTargetMessages.SelectionErrorKey(failure);
            if (failure is SelectionLimitExceededException) await RefreshAfterLimitChangeAsync(ct);
        }

        return result;
    }

    private async Task RefreshAfterLimitChangeAsync(CancellationToken ct)
    {
        var errorKey = ErrorKey;
        await PreviewAsync(1, ct);
        ErrorKey ??= errorKey;
    }

    private void Accept(SelectionPreviewSummary summary, int page)
    {
        if (Summary is not { } previous
            || previous.Preview.TruncatedByLimit != summary.Preview.TruncatedByLimit
            || previous.ToAdd != summary.ToAdd)
        {
            TruncationAccepted = false;
        }

        (Summary, PreviewPage) = (summary, page);
    }

    private TargetSelectionRequest BuildRequest(int page, bool acceptTruncation) =>
        new(_orderId, _plan.StrategyId, _plan.Parameters, page, PreviewPageSize, acceptTruncation);
}
