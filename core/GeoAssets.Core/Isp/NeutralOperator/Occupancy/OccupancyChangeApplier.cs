namespace GeoAssets.Core.Isp.NeutralOperator.Occupancy;

/// <summary>
/// Applies staged <see cref="OccupancyChange"/>s to a pole's occupancy and returns what the pole
/// then looks like (XD01-315). Pure: nothing is read or written, so the same call serves the
/// preview shown while staging and the commit made when the order is approved — the commit hands
/// <see cref="OccupancyProjection.Entries"/> to <see cref="IPoleOccupancyWriter.ReplaceEntriesAsync"/>.
///
/// <para><b>Rules</b>, each change applied in the order given, to the result of the one before:</para>
/// <list type="bullet">
///   <item><description><b>OCC-B1.</b> Add on a regulated slot that already holds an entry
///   (a reservation included) → <see cref="InvalidOperationException"/>. An Add without a slot
///   index always succeeds: it is an over-capacity entry, and a pole may hold any number of them.</description></item>
///   <item><description><b>OCC-B2.</b> Remove, Modify or MarkUnidentified of a slot holding
///   nothing, or of an entry id not on the pole → <see cref="OccupancyEntryNotFoundException"/>
///   (a <see cref="KeyNotFoundException"/>).</description></item>
///   <item><description><b>OCC-B3.</b> On a regulated slot the status may only move along
///   <see cref="PoleAttachmentSlotTransitions"/>, from what the baseline holds on that slot to
///   what the result holds → <see cref="InvalidPoleAttachmentSlotTransitionException"/>
///   otherwise. Over-capacity entries, a slot that is emptied and a slot that is newly filled
///   are outside that graph, exactly as in <see cref="PoleOccupancy.Revise"/>.</description></item>
///   <item><description><b>OCC-B4.</b> MarkUnidentified clears the occupant organization and
///   the CNPJ and sets <see cref="PoleAttachmentOccupantStatus.ClandestineUnauthorized"/>;
///   everything else on the entry stays.</description></item>
///   <item><description>The result must be a valid pole by <see cref="PoleOccupancy.Create"/>'s
///   rules (ids unique, lengths, measurements with at most three decimals, no reservation
///   beyond the regulated slots, …), which throw as documented there.</description></item>
/// </list>
/// </summary>
public static class OccupancyChangeApplier
{
    /// <summary>Applies a change set to the occupancy of the pole it was staged for.</summary>
    /// <param name="baseline">The pole's occupancy to apply to; null when the pole has none on file.</param>
    /// <param name="changeSet">The staged changes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="changeSet"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="baseline"/> is of another pole or another pole owner.</exception>
    public static OccupancyProjection Apply(PoleOccupancy? baseline, OccupancyChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        return Apply(baseline, changeSet.PoleFeatureId, changeSet.PoleOwnerOrganizationId, changeSet.Changes);
    }

    /// <summary>Applies <paramref name="changes"/> to the occupancy of one pole.</summary>
    /// <param name="baseline">The pole's occupancy to apply to; null when the pole has none on file.</param>
    /// <param name="poleFeatureId">The pole.</param>
    /// <param name="poleOwnerOrganizationId">The organization that owns it.</param>
    /// <param name="changes">The changes, applied in order. May be empty (the result is the baseline).</param>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="poleFeatureId"/> is blank; <paramref name="baseline"/> is of another pole
    /// or another pole owner; a change is malformed (see <see cref="OccupancyChange"/>).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A change names a slot outside 1..<see cref="PoleAttachmentSlotMatrix.MaxSlots"/>.</exception>
    public static OccupancyProjection Apply(
        PoleOccupancy? baseline,
        string poleFeatureId,
        Guid poleOwnerOrganizationId,
        IReadOnlyCollection<OccupancyChange> changes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poleFeatureId);
        ArgumentNullException.ThrowIfNull(changes);
        EnsureSamePole(baseline, poleFeatureId, poleOwnerOrganizationId);

        var entries = baseline is null ? [] : baseline.Entries.ToList();
        foreach (var change in changes)
        {
            OccupancyChangeRules.CheckShape(change);
            ApplyOne(entries, change, poleFeatureId, poleOwnerOrganizationId);
        }

        // Create and Revise are what enforce the entry rules and, on regulated slots, the status
        // graph; the version and instant they are given are not part of the projection.
        var result = baseline is null
            ? PoleOccupancy.Create(poleFeatureId, poleOwnerOrganizationId, entries, PoleOccupancy.FirstVersion, default)
            : baseline.Revise(entries, baseline.UpdatedAt);

        return OccupancyProjection.From(result);
    }

    internal static void EnsureSamePole(PoleOccupancy? baseline, string poleFeatureId, Guid poleOwnerOrganizationId)
    {
        if (baseline is null) return;

        if (!string.Equals(baseline.PoleFeatureId, poleFeatureId, StringComparison.Ordinal)
            || baseline.PoleOwnerOrganizationId != poleOwnerOrganizationId)
        {
            throw new ArgumentException(
                $"The baseline is the occupancy of pole '{baseline.PoleFeatureId}', not of pole '{poleFeatureId}' of the same owner.",
                nameof(baseline));
        }
    }

    private static void ApplyOne(List<PoleOccupancyEntry> entries, OccupancyChange change, string poleFeatureId, Guid owner)
    {
        if (change.Kind == OccupancyChangeKind.Add)
        {
            if (change.SlotIndex is { } slotIndex && entries.Exists(e => e.SlotIndex == slotIndex))
                throw new InvalidOperationException(
                    $"Regulated slot {slotIndex} of pole '{poleFeatureId}' is already occupied; modify or remove its entry instead of adding one.");

            entries.Add(WithPayload(
                new PoleOccupancyEntry
                {
                    Id = change.EntryId.GetValueOrDefault(),
                    PoleFeatureId = poleFeatureId,
                    PoleOwnerOrganizationId = owner,
                    SlotIndex = change.SlotIndex,
                    Status = change.Status.GetValueOrDefault(),
                },
                change));
            return;
        }

        var index = change.SlotIndex is { } slot
            ? entries.FindIndex(e => e.SlotIndex == slot)
            : entries.FindIndex(e => e.Id == change.EntryId);
        if (index < 0)
        {
            throw change.SlotIndex is { } missingSlot
                ? new OccupancyEntryNotFoundException(poleFeatureId, missingSlot)
                : new OccupancyEntryNotFoundException(poleFeatureId, change.EntryId.GetValueOrDefault());
        }

        switch (change.Kind)
        {
            case OccupancyChangeKind.Remove:
                entries.RemoveAt(index);
                break;
            case OccupancyChangeKind.Modify:
                entries[index] = WithPayload(entries[index], change);
                break;
            default:
                entries[index] = entries[index] with
                {
                    OccupantOrganizationId = null,
                    OperatorCnpj = null,
                    Status = PoleAttachmentOccupantStatus.ClandestineUnauthorized,
                };
                break;
        }
    }

    private static PoleOccupancyEntry WithPayload(PoleOccupancyEntry entry, OccupancyChange change) => entry with
    {
        Status = change.Status.GetValueOrDefault(),
        OccupantOrganizationId = change.OccupantOrganizationId,
        OperatorCnpj = change.OperatorCnpj,
        AnatelLicenseNumber = change.AnatelLicenseNumber,
        CableDiameterMm = change.CableDiameterMm,
        CableLoadDaN = change.CableLoadDaN,
        AttachedCableFeatureId = change.AttachedCableFeatureId,
    };
}
