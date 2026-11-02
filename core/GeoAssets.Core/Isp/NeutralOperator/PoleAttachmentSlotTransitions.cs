namespace GeoAssets.Core.Isp.NeutralOperator;

/// <summary>
/// The allowed <see cref="PoleAttachmentOccupantStatus"/> transition graph for a
/// <see cref="PoleAttachmentSlot"/> (XD01-258's "Occupant status transitions are validated
/// against the allowed set" AC).
///
/// <strong>Reconstruction notice:</strong> no legal/regulatory document in this repository or
/// environment enumerates the real allowed transitions — this is a best-effort, defensible
/// reconstruction from the four statuses' own meaning and this epic's later children (XD01-263's
/// clandestine-detection/30-day-notice flow, XD01-264's cleanup-drive cut orders), not a cited
/// source. Confirm against a real compliance/legal reference before treating this as ground truth.
///
/// <list type="bullet">
///   <item><description><c>ActiveReservation → AuthorizedContracted</c>: a tenant signs for a reserved slot.</description></item>
///   <item><description><c>ActiveReservation → ClandestineUnauthorized</c>: someone attaches to a reserved slot without authorization.</description></item>
///   <item><description><c>AuthorizedContracted → AbandonedDeadDrop</c>: the contract ends but the cable is never removed.</description></item>
///   <item><description><c>AuthorizedContracted → ClandestineUnauthorized</c>: the contract lapses but the operator keeps using the slot without renewing.</description></item>
///   <item><description><c>AbandonedDeadDrop → ClandestineUnauthorized</c>: a different operator attaches to an abandoned slot without authorization.</description></item>
///   <item><description><c>AbandonedDeadDrop → ActiveReservation</c>: the dead cable is physically removed, freeing the slot.</description></item>
///   <item><description><c>ClandestineUnauthorized → AuthorizedContracted</c>: regularized after the XD01-263 notice process.</description></item>
///   <item><description><c>ClandestineUnauthorized → ActiveReservation</c>: removed via an XD01-264 cleanup-drive cut order.</description></item>
/// </list>
///
/// Same-state "transitions" (re-confirming an unchanged status on re-audit) are always valid.
/// </summary>
public static class PoleAttachmentSlotTransitions
{
    private static readonly HashSet<(PoleAttachmentOccupantStatus From, PoleAttachmentOccupantStatus To)> Allowed = new()
    {
        (PoleAttachmentOccupantStatus.ActiveReservation, PoleAttachmentOccupantStatus.AuthorizedContracted),
        (PoleAttachmentOccupantStatus.ActiveReservation, PoleAttachmentOccupantStatus.ClandestineUnauthorized),
        (PoleAttachmentOccupantStatus.AuthorizedContracted, PoleAttachmentOccupantStatus.AbandonedDeadDrop),
        (PoleAttachmentOccupantStatus.AuthorizedContracted, PoleAttachmentOccupantStatus.ClandestineUnauthorized),
        (PoleAttachmentOccupantStatus.AbandonedDeadDrop, PoleAttachmentOccupantStatus.ClandestineUnauthorized),
        (PoleAttachmentOccupantStatus.AbandonedDeadDrop, PoleAttachmentOccupantStatus.ActiveReservation),
        (PoleAttachmentOccupantStatus.ClandestineUnauthorized, PoleAttachmentOccupantStatus.AuthorizedContracted),
        (PoleAttachmentOccupantStatus.ClandestineUnauthorized, PoleAttachmentOccupantStatus.ActiveReservation),
    };

    public static bool IsValid(PoleAttachmentOccupantStatus from, PoleAttachmentOccupantStatus to) =>
        from == to || Allowed.Contains((from, to));
}
