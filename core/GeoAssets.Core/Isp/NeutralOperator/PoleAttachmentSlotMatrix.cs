using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.Core.Isp.NeutralOperator;

/// <summary>
/// The 6-slot attachment-occupancy aggregate for one pole's 500mm telecom band (XD01-258).
/// Enforces the slot-matrix invariants (max 6, unique index, validated status transitions);
/// attached-cable topology itself is read through <see cref="TopoGraph"/> via
/// <see cref="SupportsEdgeKind"/> <c>TopoEdge</c>s from the pole feature — this aggregate
/// doesn't own or mutate that graph, only the slot bookkeeping layered on top of it.
/// </summary>
public sealed class PoleAttachmentSlotMatrix
{
    public const int MaxSlots = 6;

    /// <summary>The <c>TopoEdge.Kind</c> linking a pole <c>GeoFeature</c> to each attached
    /// telecom-cable feature.</summary>
    public const string SupportsEdgeKind = "supports";

    private readonly List<PoleAttachmentSlot> _slots = [];

    public required string PoleFeatureId { get; init; }

    public IReadOnlyList<PoleAttachmentSlot> Slots => _slots;

    /// <summary>Adds a new slot. Throws when <paramref name="slot"/>'s index is out of range
    /// [1, <see cref="MaxSlots"/>], already occupied, or the pole already has 6 slots.</summary>
    public void AddSlot(PoleAttachmentSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);

        if (slot.SlotIndex < 1 || slot.SlotIndex > MaxSlots)
            throw new ArgumentOutOfRangeException(
                nameof(slot), slot.SlotIndex, $"Slot index must be between 1 and {MaxSlots}.");

        if (_slots.Count >= MaxSlots)
            throw new InvalidOperationException(
                $"Pole '{PoleFeatureId}' already has the maximum of {MaxSlots} slots.");

        if (_slots.Any(s => s.SlotIndex == slot.SlotIndex))
            throw new InvalidOperationException(
                $"Slot index {slot.SlotIndex} is already occupied on pole '{PoleFeatureId}'.");

        _slots.Add(slot);
    }

    /// <summary>Changes a slot's status, enforcing <see cref="PoleAttachmentSlotTransitions"/>.</summary>
    public void TransitionStatus(int slotIndex, PoleAttachmentOccupantStatus newStatus)
    {
        var slot = _slots.FirstOrDefault(s => s.SlotIndex == slotIndex)
            ?? throw new KeyNotFoundException($"Pole '{PoleFeatureId}' has no slot at index {slotIndex}.");

        if (!PoleAttachmentSlotTransitions.IsValid(slot.Status, newStatus))
            throw new InvalidPoleAttachmentSlotTransitionException(slot.Status, newStatus);

        slot.Status = newStatus;
    }

    /// <summary>The telecom-cable features this pole physically supports, via its
    /// <see cref="SupportsEdgeKind"/> <c>TopoEdge</c>s.</summary>
    public IReadOnlyList<GeoFeature> GetAttachedCableFeatures(IEnumerable<GeoFeature> features) =>
        TopoGraph.GetNeighbors(PoleFeatureId, features, SupportsEdgeKind);
}
