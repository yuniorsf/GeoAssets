namespace GeoAssets.Core.Isp.NeutralOperator.Occupancy;

/// <summary>
/// The occupancy changes proposed for one pole through one target of one service order
/// (XD01-315). Staging never touches the pole's live occupancy: the set waits, as
/// <see cref="OccupancyChangeSetStatus.Staged"/>, until the order is approved (then it is
/// committed) or it is dropped (<see cref="Discard"/>). Immutable; every operation returns a
/// new instance or this one.
///
/// <para>It records the pole's <see cref="PoleOccupancy.Version"/> at the moment it was staged
/// (<see cref="BaselineVersion"/>, OCC-B8). A pole that has moved on since then makes the set
/// <i>stale</i>: still readable and replaceable, but a commit must refuse it.</para>
///
/// <para>It belongs to the pole owner (<see cref="PoleOwnerOrganizationId"/>), like the
/// occupancy it proposes to change.</para>
/// </summary>
public sealed class OccupancyChangeSet
{
    /// <summary>The most changes one set may hold: one per regulated slot and room for many over-capacity entries.</summary>
    public const int MaxChanges = 200;

    /// <summary>Longest <see cref="OrderId"/> / <see cref="TargetId"/> a store accepts.</summary>
    public const int MaxOrderKeyLength = 36;

    /// <summary>Longest <see cref="StagedBy"/> a store accepts.</summary>
    public const int MaxStagedByLength = 256;

    private OccupancyChangeSet(
        Guid id, string orderId, string targetId, string poleFeatureId, Guid poleOwnerOrganizationId,
        IReadOnlyList<OccupancyChange> changes, int baselineVersion, OccupancyChangeSetStatus status,
        string stagedBy, DateTimeOffset stagedAt, DateTimeOffset? closedAt)
    {
        Id = id;
        OrderId = orderId;
        TargetId = targetId;
        PoleFeatureId = poleFeatureId;
        PoleOwnerOrganizationId = poleOwnerOrganizationId;
        Changes = changes;
        BaselineVersion = baselineVersion;
        Status = status;
        StagedBy = stagedBy;
        StagedAt = stagedAt;
        ClosedAt = closedAt;
    }

    /// <summary>Identity of this set; a set that replaces another has a new one.</summary>
    public Guid Id { get; }

    /// <summary>The service order the changes were staged on.</summary>
    public string OrderId { get; }

    /// <summary>The order's target (<c>ServiceOrderTarget.Id</c>) the changes were staged on.</summary>
    public string TargetId { get; }

    /// <summary>The pole: the target's feature.</summary>
    public string PoleFeatureId { get; }

    /// <summary>The organization that owns the pole, its occupancy and this set.</summary>
    public Guid PoleOwnerOrganizationId { get; }

    /// <summary>The changes, in the order they are applied. Never empty.</summary>
    public IReadOnlyList<OccupancyChange> Changes { get; }

    /// <summary>
    /// The pole's <see cref="PoleOccupancy.Version"/> when the set was staged;
    /// <see cref="PoleOccupancy.UnsavedVersion"/> when the pole had no occupancy on file.
    /// </summary>
    public int BaselineVersion { get; }

    public OccupancyChangeSetStatus Status { get; }

    /// <summary>User id of whoever staged the set.</summary>
    public string StagedBy { get; }

    /// <summary>When the set was staged.</summary>
    public DateTimeOffset StagedAt { get; }

    /// <summary>When the set was committed or discarded; null while it is staged.</summary>
    public DateTimeOffset? ClosedAt { get; }

    /// <summary>True when the pole's occupancy is no longer at the version this set was staged against.</summary>
    /// <param name="liveVersion">The pole's current version; <see cref="PoleOccupancy.UnsavedVersion"/> when it has none on file.</param>
    public bool IsStaleAgainst(int liveVersion) => liveVersion != BaselineVersion;

    /// <summary>
    /// Stages <paramref name="changes"/> for one target of an order, against the pole's current
    /// occupancy. Checks the changes as far as they can be checked without applying them; whether
    /// they apply cleanly (a free slot for an Add, a legal status move, …) is
    /// <see cref="OccupancyChangeApplier"/>'s part.
    /// </summary>
    /// <param name="orderId">The service order.</param>
    /// <param name="targetId">The order's target.</param>
    /// <param name="poleFeatureId">The target's feature: the pole.</param>
    /// <param name="poleOwnerOrganizationId">The organization that owns the pole.</param>
    /// <param name="changes">The changes; at least one, at most <see cref="MaxChanges"/>.</param>
    /// <param name="baseline">The pole's occupancy right now; null when it has none on file.</param>
    /// <param name="stagedBy">User id of whoever stages.</param>
    /// <param name="stagedAt">The staging instant, from the caller's clock.</param>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is null (OCC-G1).</exception>
    /// <exception cref="ArgumentException">
    /// OCC-G1: <paramref name="orderId"/>, <paramref name="targetId"/>, <paramref name="poleFeatureId"/>
    /// or <paramref name="stagedBy"/> is blank or longer than a store accepts, or
    /// <paramref name="baseline"/> is of another pole or owner. OCC-G2: <paramref name="changes"/>
    /// is empty, or holds more than <see cref="MaxChanges"/>. A change is malformed (see
    /// <see cref="OccupancyChange"/>), or an Add reuses the id of an entry already on the pole.
    /// OCC-G4: two changes address the same regulated slot or the same entry — whether they name
    /// it the same way or one by slot and the other by the id of the entry on that slot.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">OCC-G3: a change names a regulated slot outside 1..<see cref="PoleAttachmentSlotMatrix.MaxSlots"/>.</exception>
    /// <exception cref="OccupancyEntryNotFoundException">
    /// OCC-G3: a Remove, Modify or MarkUnidentified names an entry id that is not on
    /// <paramref name="baseline"/> (a <see cref="KeyNotFoundException"/>).
    /// </exception>
    public static OccupancyChangeSet Stage(
        string orderId,
        string targetId,
        string poleFeatureId,
        Guid poleOwnerOrganizationId,
        IReadOnlyCollection<OccupancyChange> changes,
        PoleOccupancy? baseline,
        string stagedBy,
        DateTimeOffset stagedAt)
    {
        CheckKeys(orderId, targetId, poleFeatureId, stagedBy);
        ArgumentNullException.ThrowIfNull(changes);
        OccupancyChangeApplier.EnsureSamePole(baseline, poleFeatureId, poleOwnerOrganizationId);
        var checkedChanges = CheckChanges(changes, baseline, poleFeatureId);

        return new OccupancyChangeSet(
            Guid.NewGuid(), orderId, targetId, poleFeatureId, poleOwnerOrganizationId, checkedChanges,
            baseline?.Version ?? PoleOccupancy.UnsavedVersion, OccupancyChangeSetStatus.Staged, stagedBy, stagedAt, closedAt: null);
    }

    /// <summary>
    /// Stages again for the same target: the returned set — new id, new changes, the pole's
    /// current version as its baseline — replaces this one (OCC-B5). Checks as <see cref="Stage"/> does.
    /// </summary>
    /// <exception cref="InvalidOperationException">OCC-B6: this set is already committed or discarded.</exception>
    /// <remarks>Also throws everything <see cref="Stage"/> throws.</remarks>
    public OccupancyChangeSet Restage(
        IReadOnlyCollection<OccupancyChange> changes, PoleOccupancy? baseline, string stagedBy, DateTimeOffset stagedAt)
    {
        if (Status != OccupancyChangeSetStatus.Staged)
            throw new InvalidOperationException(
                $"Change set '{Id}' is {Status}; only a staged set can be staged again.");

        return Stage(OrderId, TargetId, PoleFeatureId, PoleOwnerOrganizationId, changes, baseline, stagedBy, stagedAt);
    }

    /// <summary>
    /// Drops the set (OCC-B7): a staged set becomes <see cref="OccupancyChangeSetStatus.Discarded"/>,
    /// closed at <paramref name="discardedAt"/>; an already discarded set is returned as it is.
    /// </summary>
    /// <exception cref="InvalidOperationException">The set is committed: what was applied to the pole cannot be discarded.</exception>
    public OccupancyChangeSet Discard(DateTimeOffset discardedAt) => Status switch
    {
        OccupancyChangeSetStatus.Discarded => this,
        OccupancyChangeSetStatus.Staged => new OccupancyChangeSet(
            Id, OrderId, TargetId, PoleFeatureId, PoleOwnerOrganizationId, Changes, BaselineVersion,
            OccupancyChangeSetStatus.Discarded, StagedBy, StagedAt, discardedAt),
        _ => throw new InvalidOperationException($"Change set '{Id}' is {Status} and cannot be discarded."),
    };

    /// <summary>
    /// Records that the set was applied to the pole's live occupancy (XD01-318): a staged set
    /// becomes <see cref="OccupancyChangeSetStatus.Committed"/>, closed at <paramref name="committedAt"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The set is already committed or discarded: only a staged set is committed, and only once.</exception>
    public OccupancyChangeSet Commit(DateTimeOffset committedAt) => Status == OccupancyChangeSetStatus.Staged
        ? new OccupancyChangeSet(
            Id, OrderId, TargetId, PoleFeatureId, PoleOwnerOrganizationId, Changes, BaselineVersion,
            OccupancyChangeSetStatus.Committed, StagedBy, StagedAt, committedAt)
        : throw new InvalidOperationException($"Change set '{Id}' is {Status}; only a staged set can be committed.");

    /// <summary>
    /// Rebuilds a set from stored data, in whatever status it was stored. For stores; it checks
    /// what must hold of any set but not the changes against a pole (the pole may have moved on).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is empty; a key is blank or over-long; <paramref name="changes"/>
    /// is empty or too many, or holds a malformed change; a staged set has a closing instant, or
    /// a committed or discarded one has none.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="baselineVersion"/> is negative; <paramref name="status"/> is not a defined
    /// value; a change names a slot outside the regulated range.
    /// </exception>
    public static OccupancyChangeSet Restore(
        Guid id,
        string orderId,
        string targetId,
        string poleFeatureId,
        Guid poleOwnerOrganizationId,
        IReadOnlyCollection<OccupancyChange> changes,
        int baselineVersion,
        OccupancyChangeSetStatus status,
        string stagedBy,
        DateTimeOffset stagedAt,
        DateTimeOffset? closedAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("A change set id cannot be empty.", nameof(id));
        CheckKeys(orderId, targetId, poleFeatureId, stagedBy);
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentOutOfRangeException.ThrowIfNegative(baselineVersion);
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown change set status.");
        if (status == OccupancyChangeSetStatus.Staged != closedAt is null)
            throw new ArgumentException("A staged set has no closing instant; a committed or discarded set has one.", nameof(closedAt));

        var checkedChanges = CheckChanges(changes, baseline: null, poleFeatureId, againstBaseline: false);
        return new OccupancyChangeSet(
            id, orderId, targetId, poleFeatureId, poleOwnerOrganizationId, checkedChanges,
            baselineVersion, status, stagedBy, stagedAt, closedAt);
    }

    private static void CheckKeys(string orderId, string targetId, string poleFeatureId, string stagedBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(poleFeatureId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedBy);

        if (orderId.Length > MaxOrderKeyLength)
            throw new ArgumentException($"An order id is at most {MaxOrderKeyLength} characters.", nameof(orderId));
        if (targetId.Length > MaxOrderKeyLength)
            throw new ArgumentException($"A target id is at most {MaxOrderKeyLength} characters.", nameof(targetId));
        if (poleFeatureId.Length > PoleOccupancyEntry.MaxFeatureIdLength)
            throw new ArgumentException(
                $"A pole feature id is at most {PoleOccupancyEntry.MaxFeatureIdLength} characters.", nameof(poleFeatureId));
        if (stagedBy.Length > MaxStagedByLength)
            throw new ArgumentException($"A user id is at most {MaxStagedByLength} characters.", nameof(stagedBy));
    }

    /// <summary>
    /// Shape of every change, entry references against <paramref name="baseline"/> (when asked),
    /// and no two changes on the same slot or entry.
    /// </summary>
    private static List<OccupancyChange> CheckChanges(
        IReadOnlyCollection<OccupancyChange> changes, PoleOccupancy? baseline, string poleFeatureId, bool againstBaseline = true)
    {
        if (changes.Count == 0)
            throw new ArgumentException("A change set must hold at least one change; discard the set to drop its changes.", nameof(changes));
        if (changes.Count > MaxChanges)
            throw new ArgumentException($"A change set holds at most {MaxChanges} changes.", nameof(changes));

        var entries = baseline?.Entries ?? [];
        var slots = new HashSet<int>();
        var entryIds = new HashSet<Guid>();
        var checkedChanges = new List<OccupancyChange>(changes.Count);
        foreach (var change in changes)
        {
            OccupancyChangeRules.CheckShape(change);

            var (slotIndex, entryId) = againstBaseline
                ? Resolve(change, entries, poleFeatureId)
                : (change.SlotIndex, change.EntryId);
            if ((slotIndex is { } slot && !slots.Add(slot)) || (entryId is { } id && !entryIds.Add(id)))
            {
                throw new ArgumentException(
                    $"Two changes address the same {(slotIndex is null ? $"entry '{entryId}'" : $"regulated slot {slotIndex}")}; stage one change per slot or entry.",
                    nameof(changes));
            }

            checkedChanges.Add(change);
        }

        return checkedChanges;
    }

    /// <summary>
    /// The slot and the entry a change touches on <paramref name="entries"/>, so that "slot 3"
    /// and "the entry sitting on slot 3" are recognised as the same address.
    /// </summary>
    private static (int? SlotIndex, Guid? EntryId) Resolve(
        OccupancyChange change, IReadOnlyList<PoleOccupancyEntry> entries, string poleFeatureId)
    {
        if (change.Kind == OccupancyChangeKind.Add)
        {
            if (entries.Any(e => e.Id == change.EntryId))
                throw new ArgumentException(
                    $"An Add reuses the id of entry '{change.EntryId}', which is already on pole '{poleFeatureId}'.", nameof(change));

            return (change.SlotIndex, change.EntryId);
        }

        if (change.SlotIndex is { } slotIndex)
            return (slotIndex, entries.FirstOrDefault(e => e.SlotIndex == slotIndex)?.Id);

        var entryId = change.EntryId.GetValueOrDefault();
        var entry = entries.FirstOrDefault(e => e.Id == entryId)
            ?? throw new OccupancyEntryNotFoundException(poleFeatureId, entryId);
        return (entry.SlotIndex, entryId);
    }
}
