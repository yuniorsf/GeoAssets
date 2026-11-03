using GeoAssets.Core.Isp.NeutralOperator.Evidence;

namespace GeoAssets.Workflow.Isp.NeutralOperator;

/// <summary>
/// Batches expired, unregularized <c>CABO_A_REVELIA</c> clandestine-attachment notices
/// (XD01-263) along a street corridor into one multi-pole <c>CUT_AND_REMOVE_OS</c> order per
/// corridor, dispatches it to transit/utility/crew, and later closes it once a sealed
/// post-removal photo confirms the cables were actually cut (XD01-264).
/// </summary>
public interface ICleanupDriveBatchingService
{
    /// <summary>
    /// Finds every clandestine notice past its 30-day deadline as of <paramref name="asOfUtc"/>
    /// (via <see cref="IClandestineAttachmentService.GetElapsedUnregularizedAsync"/>), groups
    /// their poles into street-corridor batches (single-linkage spatial clustering — see
    /// <see cref="CleanupDriveBatchingService"/>'s doc comment for why and the chosen
    /// threshold), creates one <c>CUT_AND_REMOVE_OS</c> order per batch, and dispatches it with
    /// three distinct agency notifications (municipal transit, electric distributor,
    /// neutral-operator crew).
    /// </summary>
    Task<IReadOnlyList<CutAndRemoveBatchResult>> BatchAndDispatchExpiredNoticesAsync(
        DateTimeOffset asOfUtc, string? performedBy = null, CancellationToken ct = default);

    /// <summary>
    /// Closes a dispatched cut order: rejects with
    /// <see cref="CutOrderMissingSealedEvidenceException"/> unless <paramref name="evidence"/>
    /// verifies against <paramref name="deviceSecret"/>, otherwise releases every pole slot in
    /// <paramref name="releases"/> back to <c>ActiveReservation</c> (XD01-258's
    /// "freed slot" state — see <see cref="PoleSlotRelease"/>'s doc comment on why the caller,
    /// not this service, owns loading/persisting the real slot matrices) and transitions the
    /// order to <see cref="CutAndRemoveWorkflowGraph.Completed"/>.
    /// </summary>
    Task CompleteCutOrderAsync(
        string serviceOrderId,
        IReadOnlyList<PoleSlotRelease> releases,
        FieldPhotoEvidenceSubmission evidence,
        string deviceSecret,
        string? performedBy = null,
        CancellationToken ct = default);
}
