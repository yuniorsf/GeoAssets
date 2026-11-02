namespace GeoAssets.Core.Isp.NeutralOperator;

/// <summary>
/// Occupancy state of one <see cref="PoleAttachmentSlot"/> in a Neutral Infrastructure
/// Operator's 500mm telecom band (XD01-258). Transition legality between these is enforced by
/// <see cref="PoleAttachmentSlotTransitions.IsValid"/>.
/// </summary>
public enum PoleAttachmentOccupantStatus
{
    /// <summary>Reserved for a future tenant, not yet physically occupied.</summary>
    ActiveReservation,

    /// <summary>A cable is attached under a signed contract with a known operator/ANATEL license.</summary>
    AuthorizedContracted,

    /// <summary>A contract ended but the cable was never removed.</summary>
    AbandonedDeadDrop,

    /// <summary>A cable is attached with no authorization on file — the XD01-263 detection target.</summary>
    ClandestineUnauthorized,
}
