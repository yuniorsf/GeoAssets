namespace GeoAssets.Domain.NeutralPole.SlotAllocation;

/// <summary>
/// Tracks which operators currently occupy a neutral pole's shared attachment band, under
/// Decreto Federal nº 12.068/2024's shared-infrastructure regime.
///
/// <see cref="MaxSlots"/> defaults to 6 — the decree's stated range is "5-6 operators"
/// depending on pole classification, so this takes it as a constructor parameter rather than
/// hardcoding a single number; pass 5 for a pole class where that's the actual limit.
///
/// Competitor isolation (XD01-211's AC: "Competitor-tenant data never crosses in query
/// results"): this type exposes two different read surfaces on purpose.
/// <see cref="GetOccupancySummary"/> returns only slot indices and an occupied flag — safe for
/// any operator sharing the pole to see, since it never names who holds a given slot.
/// <see cref="GetAllocations"/> returns the full operator-identified detail and is meant for
/// admin/regulator contexts only — the REST layer exposing this assembly (a later ticket) must
/// gate it behind proper authorization; this pure-domain type has no concept of "caller" to
/// enforce that itself.
/// </summary>
public sealed class PoleSlotAllocationBoard
{
    private readonly Dictionary<int, string> _occupantBySlot = [];

    public int MaxSlots { get; }

    public PoleSlotAllocationBoard(int maxSlots = 6)
    {
        if (maxSlots < 1) throw new ArgumentOutOfRangeException(nameof(maxSlots), "A pole must have at least one attachment slot.");
        MaxSlots = maxSlots;
    }

    public int OccupiedCount => _occupantBySlot.Count;
    public int AvailableCount => MaxSlots - OccupiedCount;

    /// <summary>
    /// Allocates the lowest-numbered free slot to <paramref name="operatorId"/>. Allocating
    /// again for an operator that already holds a slot is a no-op (returns its existing slot,
    /// <see cref="SlotAllocationOutcome.AlreadyAllocated"/>) rather than consuming a second one.
    /// </summary>
    public SlotAllocationResult Allocate(string operatorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        var existing = _occupantBySlot.FirstOrDefault(kvp => string.Equals(kvp.Value, operatorId, StringComparison.Ordinal));
        if (existing.Value is not null)
            return new SlotAllocationResult(SlotAllocationOutcome.AlreadyAllocated, existing.Key);

        if (OccupiedCount >= MaxSlots)
            return new SlotAllocationResult(SlotAllocationOutcome.PoleFull, SlotIndex: null);

        for (var slotIndex = 1; slotIndex <= MaxSlots; slotIndex++)
        {
            if (_occupantBySlot.ContainsKey(slotIndex)) continue;
            _occupantBySlot[slotIndex] = operatorId;
            return new SlotAllocationResult(SlotAllocationOutcome.Allocated, slotIndex);
        }

        // Unreachable given the OccupiedCount >= MaxSlots check above, but keeps the compiler
        // (and a future refactor) honest that every path returns.
        return new SlotAllocationResult(SlotAllocationOutcome.PoleFull, SlotIndex: null);
    }

    /// <summary>Frees whatever slot <paramref name="operatorId"/> holds. A no-op if it holds none.</summary>
    public bool Release(string operatorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        var slot = _occupantBySlot.FirstOrDefault(kvp => string.Equals(kvp.Value, operatorId, StringComparison.Ordinal));
        if (slot.Value is null) return false;

        return _occupantBySlot.Remove(slot.Key);
    }

    /// <summary>Full, operator-identified detail. Admin/regulator use only — see the class doc comment.</summary>
    public IReadOnlyList<PoleAttachmentSlot> GetAllocations() =>
        Enumerable.Range(1, MaxSlots)
            .Select(i => new PoleAttachmentSlot(i, _occupantBySlot.GetValueOrDefault(i)))
            .ToList();

    /// <summary>Anonymized occupancy — safe to show any operator sharing the pole. See the class doc comment.</summary>
    public IReadOnlyList<SlotOccupancySummary> GetOccupancySummary() =>
        Enumerable.Range(1, MaxSlots)
            .Select(i => new SlotOccupancySummary(i, _occupantBySlot.ContainsKey(i)))
            .ToList();
}
