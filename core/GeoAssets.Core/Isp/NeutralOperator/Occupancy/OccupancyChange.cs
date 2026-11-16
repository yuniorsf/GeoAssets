namespace GeoAssets.Core.Isp.NeutralOperator.Occupancy;

/// <summary>
/// One proposed edit to a pole's occupancy, staged on a service order's target and applied to
/// the pole only when the order is approved (XD01-315). A value: it is checked when it enters an
/// <see cref="OccupancyChangeSet"/> and when <see cref="OccupancyChangeApplier"/> applies it.
///
/// <para><b>What it addresses.</b></para>
/// <list type="bullet">
///   <item><description><see cref="OccupancyChangeKind.Add"/> creates an entry whose id is
///   <see cref="EntryId"/> (required, chosen by whoever stages, so a preview and the later
///   commit produce the same entry). With a <see cref="SlotIndex"/> the entry goes on that
///   regulated slot; without one it is an over-capacity entry, beyond the six regulated slots.
///   A pole may be given any number of those: more than six occupied points is an irregularity
///   to record, never a reason to refuse.</description></item>
///   <item><description><see cref="OccupancyChangeKind.Remove"/>,
///   <see cref="OccupancyChangeKind.Modify"/> and <see cref="OccupancyChangeKind.MarkUnidentified"/>
///   address one existing entry by <b>exactly one</b> of <see cref="SlotIndex"/> (the entry on
///   that regulated slot) or <see cref="EntryId"/> (that entry, wherever it sits — the only way
///   to reach an over-capacity entry).</description></item>
/// </list>
///
/// <para><b>Payload.</b> <see cref="Status"/> and the fields after it are the entry's data. Add
/// and Modify use all of them, as a whole (Modify does not merge: a field left null is cleared);
/// both require <see cref="Status"/>. Remove and MarkUnidentified ignore the payload.</para>
/// </summary>
public sealed record OccupancyChange
{
    public required OccupancyChangeKind Kind { get; init; }

    /// <summary>The regulated slot, 1..<see cref="PoleAttachmentSlotMatrix.MaxSlots"/>; null when the change goes by <see cref="EntryId"/> or adds an over-capacity entry.</summary>
    public int? SlotIndex { get; init; }

    /// <summary>The id of the entry to create (Add) or the existing entry addressed (the other kinds); never <see cref="Guid.Empty"/>.</summary>
    public Guid? EntryId { get; init; }

    /// <summary>The entry's status after the change; required for Add and Modify.</summary>
    public PoleAttachmentOccupantStatus? Status { get; init; }

    /// <summary>The occupant organization; null with a null <see cref="OperatorCnpj"/> means an unidentified occupant.</summary>
    public Guid? OccupantOrganizationId { get; init; }

    public string? OperatorCnpj { get; init; }

    public string? AnatelLicenseNumber { get; init; }

    public decimal CableDiameterMm { get; init; }

    public decimal CableLoadDaN { get; init; }

    public string? AttachedCableFeatureId { get; init; }

    /// <summary>True for an Add without a slot index: a new entry beyond the regulated slots.</summary>
    public bool AddsBeyondRegulatedSlots => Kind == OccupancyChangeKind.Add && SlotIndex is null;
}
