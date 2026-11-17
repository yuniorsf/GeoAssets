using GeoAssets.Core.Models;
using GeoAssets.Workflow.Rules;

namespace GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;

/// <summary>
/// Reads what dispatching a shared-use order would do (XD01-316), without doing it. Every member
/// computes the plan afresh from the order's targets and the live occupancy of its poles, checks
/// the caller exactly as <see cref="ISharedUseDispatchService.DispatchAsync"/> does, and writes
/// nothing. <paramref name="page"/> is 1-based and <paramref name="pageSize"/> is 1 to 200; a
/// value outside that throws <see cref="ArgumentOutOfRangeException"/>, and a page past the end
/// is empty with the true total.
/// </summary>
public interface ISharedUseDispatchPlanReader
{
    /// <summary>
    /// The plan's counts and one page of its recipients. An order that cannot be dispatched as it
    /// stands is not an error here: the preview says why in
    /// <see cref="SharedUseDispatchPreview.BlockReason"/>.
    /// </summary>
    Task<SharedUseDispatchPreview> PreviewAsync(
        string orderId, int page, int pageSize, WorkflowPrincipal principal, CancellationToken ct);

    /// <summary>
    /// One page of the occupancy entries that get no dispatch, ordered by pole feature id, then by
    /// the entry id's canonical text.
    /// </summary>
    Task<PagedResult<SharedUseSkippedEntry>> GetSkippedEntriesAsync(
        string orderId, int page, int pageSize, WorkflowPrincipal principal, CancellationToken ct);

    /// <summary>One page of the feature ids of the poles with nobody to notify, in ordinal order.</summary>
    Task<PagedResult<string>> GetUnnotifiablePolesAsync(
        string orderId, int page, int pageSize, WorkflowPrincipal principal, CancellationToken ct);
}
