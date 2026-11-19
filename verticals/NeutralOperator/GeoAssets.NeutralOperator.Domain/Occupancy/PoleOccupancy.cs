namespace GeoAssets.NeutralOperator.Domain.Occupancy;

/// <summary>
/// Everything on file about who is attached to one pole (XD01-314): its entries on the six
/// regulated slots, its over-capacity entries beyond them, and the <see cref="Version"/> writers
/// compare to detect each other. Immutable; a write produces a new instance
/// (<see cref="Revise"/>).
///
/// This sits beside <see cref="PoleAttachmentSlotMatrix"/>, which keeps its limit of 6 and its
/// behaviour. <see cref="ToSlotMatrix"/> projects the regulated entries onto that matrix for the
/// code that already works on it (GIS export, lease billing); over-capacity entries have no
/// place in a matrix and stay here, in <see cref="OverCapacityEntries"/>.
/// </summary>
public sealed class PoleOccupancy
{
    /// <summary>The version to expect when a pole has no occupancy on file yet.</summary>
    public const int UnsavedVersion = 0;

    /// <summary>The version of a pole's occupancy after its first write.</summary>
    public const int FirstVersion = 1;

    private PoleOccupancy(
        string poleFeatureId,
        Guid poleOwnerOrganizationId,
        IReadOnlyList<PoleOccupancyEntry> entries,
        int version,
        DateTimeOffset updatedAt)
    {
        PoleFeatureId = poleFeatureId;
        PoleOwnerOrganizationId = poleOwnerOrganizationId;
        Entries = entries;
        Version = version;
        UpdatedAt = updatedAt;
        RegulatedEntries = [.. entries.Where(e => e.IsRegulated)];
        OverCapacityEntries = [.. entries.Where(e => e.IsBeyondRegulatedSlots)];
        OccupiedCount = entries.Count(e => e.IsOccupied);
        HasUnidentifiedOccupant = entries.Any(e => e.IsUnidentified);
    }

    /// <summary>The <c>GeoFeature.Id</c> of the pole.</summary>
    public string PoleFeatureId { get; }

    /// <summary>The organization that owns the pole and this record.</summary>
    public Guid PoleOwnerOrganizationId { get; }

    /// <summary>
    /// Optimistic-concurrency version: <see cref="FirstVersion"/> after the first write, plus one
    /// on every later write, whatever the write changed.
    /// </summary>
    public int Version { get; }

    /// <summary>When this version was written.</summary>
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>
    /// Every entry: the regulated ones by ascending slot index, then the over-capacity ones by
    /// the ordinal order of their id's canonical text (<c>Guid.ToString("D")</c>).
    /// </summary>
    public IReadOnlyList<PoleOccupancyEntry> Entries { get; }

    /// <summary>The entries on regulated slots, by ascending slot index. At most 6.</summary>
    public IReadOnlyList<PoleOccupancyEntry> RegulatedEntries { get; }

    /// <summary>The entries beyond the regulated slots. Unbounded.</summary>
    public IReadOnlyList<PoleOccupancyEntry> OverCapacityEntries { get; }

    /// <summary>
    /// How many attachment points physically hold a cable, regulated or not
    /// (<see cref="PoleOccupancyEntry.IsOccupied"/>); reservations are not counted.
    /// </summary>
    public int OccupiedCount { get; }

    /// <summary>
    /// Irregularity: more than <see cref="PoleAttachmentSlotMatrix.MaxSlots"/> occupied points.
    /// It is the count that decides, not where the entries sit: a pole with five occupied slots
    /// and one over-capacity entry holds six cables and is not over capacity.
    /// </summary>
    public bool IsOverCapacity => OccupiedCount > PoleAttachmentSlotMatrix.MaxSlots;

    /// <summary>Irregularity: at least one occupied point has no occupant organization on file.</summary>
    public bool HasUnidentifiedOccupant { get; }

    /// <summary>True when the pole is in any irregular situation.</summary>
    public bool IsIrregular => IsOverCapacity || HasUnidentifiedOccupant;

    /// <summary>The most severe state, for map theming (see <see cref="OccupancyClassifier"/>).</summary>
    public OccupancyClass Class => OccupancyClassifier.Classify(OccupiedCount, HasUnidentifiedOccupant);

    /// <summary>
    /// Builds a pole's occupancy from stored data, checking everything that must hold of it.
    /// </summary>
    /// <param name="poleFeatureId">The pole.</param>
    /// <param name="poleOwnerOrganizationId">The organization that owns it.</param>
    /// <param name="entries">Its entries, in any order; may be empty.</param>
    /// <param name="version">Its version, <see cref="FirstVersion"/> or more.</param>
    /// <param name="updatedAt">When that version was written.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="poleFeatureId"/> is blank or longer than
    /// <see cref="PoleOccupancyEntry.MaxFeatureIdLength"/>; an entry is null, has an empty
    /// <see cref="PoleOccupancyEntry.Id"/> or one another entry already has, names another pole or
    /// another pole owner, has an empty (rather than null) occupant organization, a blank or
    /// over-long text field, or is an over-capacity entry in status
    /// <see cref="PoleAttachmentOccupantStatus.ActiveReservation"/> (there is no slot beyond the
    /// regulated ones to reserve).
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="version"/> is below <see cref="FirstVersion"/>; a slot index is outside
    /// 1..<see cref="PoleAttachmentSlotMatrix.MaxSlots"/>; a status is not a defined value; a
    /// cable diameter or load is negative, above its maximum, or has more than
    /// <see cref="PoleOccupancyEntry.MeasurementDecimals"/> decimal places.
    /// </exception>
    /// <exception cref="InvalidOperationException">Two entries claim the same regulated slot.</exception>
    public static PoleOccupancy Create(
        string poleFeatureId,
        Guid poleOwnerOrganizationId,
        IEnumerable<PoleOccupancyEntry> entries,
        int version,
        DateTimeOffset updatedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(poleFeatureId);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, FirstVersion);
        if (poleFeatureId.Length > PoleOccupancyEntry.MaxFeatureIdLength)
            throw new ArgumentException(
                $"A pole feature id is at most {PoleOccupancyEntry.MaxFeatureIdLength} characters.", nameof(poleFeatureId));

        var checkedEntries = new List<PoleOccupancyEntry>();
        var ids = new HashSet<Guid>();
        var slots = new HashSet<int>();
        foreach (var entry in entries)
        {
            PoleOccupancyEntryRules.Check(entry, poleFeatureId, poleOwnerOrganizationId);

            if (!ids.Add(entry.Id))
                throw new ArgumentException($"Entry id '{entry.Id}' appears more than once on pole '{poleFeatureId}'.", nameof(entries));

            if (entry.SlotIndex is { } slotIndex && !slots.Add(slotIndex))
                throw new InvalidOperationException(
                    $"Slot index {slotIndex} is already occupied on pole '{poleFeatureId}'.");

            checkedEntries.Add(entry);
        }

        checkedEntries.Sort(PoleOccupancyEntryRules.Compare);
        return new PoleOccupancy(poleFeatureId, poleOwnerOrganizationId, checkedEntries, version, updatedAt);
    }

    /// <summary>
    /// The next version of this pole's occupancy: <paramref name="entries"/> replace the current
    /// ones as a whole, and <see cref="Version"/> grows by one.
    ///
    /// On a regulated slot that both versions hold, the status may only move along
    /// <see cref="PoleAttachmentSlotTransitions"/> (keeping the status is always allowed),
    /// whoever the entry on that slot is. A slot that is emptied, a slot that is newly filled,
    /// and over-capacity entries are not subject to that graph.
    /// </summary>
    /// <exception cref="InvalidPoleAttachmentSlotTransitionException">A regulated slot's status change is not allowed.</exception>
    /// <remarks>Also throws everything <see cref="Create"/> throws for <paramref name="entries"/>.</remarks>
    public PoleOccupancy Revise(IEnumerable<PoleOccupancyEntry> entries, DateTimeOffset updatedAt)
    {
        var next = Create(PoleFeatureId, PoleOwnerOrganizationId, entries, checked(Version + 1), updatedAt);

        foreach (var current in RegulatedEntries)
        {
            var successor = next.RegulatedEntries.FirstOrDefault(e => e.SlotIndex == current.SlotIndex);
            if (successor is not null && !PoleAttachmentSlotTransitions.IsValid(current.Status, successor.Status))
                throw new InvalidPoleAttachmentSlotTransitionException(current.Status, successor.Status);
        }

        return next;
    }

    /// <summary>
    /// The regulated entries as the existing 6-slot <see cref="PoleAttachmentSlotMatrix"/> (a new
    /// instance on every call). Over-capacity entries are <b>not</b> in it — the matrix cannot
    /// hold them — and neither is <see cref="PoleOccupancyEntry.OccupantOrganizationId"/>, which
    /// a matrix slot has no field for.
    /// </summary>
    public PoleAttachmentSlotMatrix ToSlotMatrix()
    {
        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = PoleFeatureId };
        foreach (var entry in RegulatedEntries)
        {
            matrix.AddSlot(new PoleAttachmentSlot
            {
                SlotIndex = entry.SlotIndex.GetValueOrDefault(),
                Status = entry.Status,
                OperatorCnpj = entry.OperatorCnpj,
                AnatelLicenseNumber = entry.AnatelLicenseNumber,
                CableDiameterMm = entry.CableDiameterMm,
                CableLoadDaN = entry.CableLoadDaN,
                AttachedCableFeatureId = entry.AttachedCableFeatureId,
            });
        }

        return matrix;
    }
}
