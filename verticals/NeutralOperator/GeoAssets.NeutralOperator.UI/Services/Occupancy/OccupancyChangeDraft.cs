using System.Globalization;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy;

namespace GeoAssets.NeutralOperator.UI.Services.Occupancy;

/// <summary>
/// The working copy behind <c>OccupancyChangeEditor</c> (XD01-315): the changes a user is
/// putting together for one pole, on top of the pole's live occupancy, before staging them.
/// It holds at most one change per regulated slot or entry (what a change set allows), lists
/// the rows to show, and previews the result with the same <see cref="OccupancyChangeApplier"/>
/// the host runs — so what the user sees before saving is what the host will accept.
///
/// Pure: no rendering and no I/O. A new id for an added entry is passed in by the caller.
/// </summary>
public sealed class OccupancyChangeDraft
{
    private readonly PoleOccupancy? _baseline;
    private readonly IReadOnlyList<PoleOccupancyEntry> _entries;
    private readonly List<OccupancyChange> _staged;
    private readonly Dictionary<string, OccupancyChange> _changes = new(StringComparer.Ordinal);

    /// <summary>Starts from a target's view: its live occupancy and whatever is already staged on it.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="view"/> is null.</exception>
    public OccupancyChangeDraft(TargetOccupancyView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        PoleFeatureId = view.PoleFeatureId;
        PoleOwnerOrganizationId = view.PoleOwnerOrganizationId;
        _entries = view.Current.Entries;
        _baseline = view.CurrentVersion >= PoleOccupancy.FirstVersion
            ? PoleOccupancy.Create(PoleFeatureId, PoleOwnerOrganizationId, _entries, view.CurrentVersion, default)
            : null;

        _staged = [.. view.Staged?.Changes ?? []];
        foreach (var change in _staged)
            _changes[KeyOf(change)] = change;
    }

    public string PoleFeatureId { get; }

    public Guid PoleOwnerOrganizationId { get; }

    /// <summary>The pending changes: regulated slots first, by index, then entries beyond them, by key.</summary>
    public IReadOnlyList<OccupancyChange> Changes =>
    [
        .. _changes
            .OrderBy(pair => SlotOf(pair.Key) ?? int.MaxValue)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value),
    ];

    /// <summary>True when nothing is pending.</summary>
    public bool IsEmpty => _changes.Count == 0;

    /// <summary>True when the pending changes differ from what is staged on the host.</summary>
    public bool IsDirty => !Changes.SequenceEqual(new OccupancyChangeDraft(_staged, this).Changes);

    private OccupancyChangeDraft(List<OccupancyChange> staged, OccupancyChangeDraft source)
    {
        PoleFeatureId = source.PoleFeatureId;
        PoleOwnerOrganizationId = source.PoleOwnerOrganizationId;
        _baseline = source._baseline;
        _entries = source._entries;
        _staged = staged;
        foreach (var change in staged)
            _changes[KeyOf(change)] = change;
    }

    // ── Editing ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Puts <paramref name="form"/> on a row: an Add when nothing is on file there (a free slot,
    /// or an entry already being added), a Modify otherwise. Replaces what was pending on the row.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The draft has no row with that key.</exception>
    public void SetEntry(string rowKey, OccupancyEntryForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var row = Row(rowKey);

        _changes[rowKey] = row.Current is null
            ? form.ToChange(OccupancyChangeKind.Add, row.SlotIndex, row.Change?.EntryId ?? throw NoEntryId(rowKey))
            : Addressed(row, form.ToChange(OccupancyChangeKind.Modify, slotIndex: null, entryId: null));
    }

    /// <summary>Starts an Add on a free regulated slot, creating an entry with <paramref name="newEntryId"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slotIndex"/> is not a regulated slot.</exception>
    /// <exception cref="InvalidOperationException">The slot holds an entry, or already has a pending change.</exception>
    public void AddToSlot(int slotIndex, OccupancyEntryForm form, Guid newEntryId)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentOutOfRangeException.ThrowIfLessThan(slotIndex, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slotIndex, PoleAttachmentSlotMatrix.MaxSlots);

        var key = SlotKey(slotIndex);
        if (!Row(key).IsFree)
            throw new InvalidOperationException($"Regulated slot {slotIndex} is not free.");

        _changes[key] = form.ToChange(OccupancyChangeKind.Add, slotIndex, newEntryId);
    }

    /// <summary>Adds an entry beyond the regulated slots — always possible: an over-capacity pole is recorded, never refused.</summary>
    /// <returns>The key of the new row.</returns>
    public string AddBeyondRegulatedSlots(OccupancyEntryForm form, Guid newEntryId)
    {
        ArgumentNullException.ThrowIfNull(form);

        var key = EntryKey(newEntryId);
        _changes[key] = form.ToChange(OccupancyChangeKind.Add, slotIndex: null, newEntryId);
        return key;
    }

    /// <summary>Marks the row's entry for removal. On an entry that is only being added, it drops the Add instead.</summary>
    public void Remove(string rowKey) => Mark(rowKey, OccupancyChangeKind.Remove);

    /// <summary>Marks the row's entry as an unidentified occupant.</summary>
    public void MarkUnidentified(string rowKey) => Mark(rowKey, OccupancyChangeKind.MarkUnidentified);

    /// <summary>Drops whatever is pending on the row. Returns false when nothing was.</summary>
    public bool Undo(string rowKey) => _changes.Remove(rowKey);

    /// <summary>Drops every pending change.</summary>
    public void Clear() => _changes.Clear();

    // ── Reading ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The rows to show: the six regulated slots, always, then the entries beyond them — those
    /// on file by the ordinal order of their id, then the ones being added, then changes left
    /// over for entries that are no longer on the pole.
    /// </summary>
    public IReadOnlyList<OccupancyDraftRow> Rows()
    {
        TryPreview(out var preview, out _);
        var result = preview?.Entries ?? [];

        var rows = new List<OccupancyDraftRow>();
        for (var slotIndex = 1; slotIndex <= PoleAttachmentSlotMatrix.MaxSlots; slotIndex++)
        {
            var key = SlotKey(slotIndex);
            rows.Add(new OccupancyDraftRow(
                key, slotIndex, _entries.FirstOrDefault(e => e.SlotIndex == slotIndex),
                _changes.GetValueOrDefault(key), result.FirstOrDefault(e => e.SlotIndex == slotIndex)));
        }

        foreach (var entry in _entries.Where(e => e.IsBeyondRegulatedSlots))
        {
            var key = EntryKey(entry.Id);
            rows.Add(new OccupancyDraftRow(key, null, entry, _changes.GetValueOrDefault(key), result.FirstOrDefault(e => e.Id == entry.Id)));
        }

        var shown = rows.Select(row => row.Key).ToHashSet(StringComparer.Ordinal);
        rows.AddRange(_changes
            .Where(pair => !shown.Contains(pair.Key))
            .OrderBy(pair => pair.Value.Kind == OccupancyChangeKind.Add ? 0 : 1)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new OccupancyDraftRow(
                pair.Key, null, null, pair.Value, result.FirstOrDefault(e => e.Id == pair.Value.EntryId))));

        return rows;
    }

    /// <summary>
    /// The pole with the pending changes applied. Returns false, with the localization key of
    /// the reason, when they do not apply; an empty draft previews as the pole itself.
    /// </summary>
    public bool TryPreview(out OccupancyProjection? preview, out string? errorKey)
    {
        (preview, errorKey) = (null, null);
        try
        {
            preview = OccupancyChangeApplier.Apply(_baseline, PoleFeatureId, PoleOwnerOrganizationId, Changes);
            return true;
        }
        catch (OccupancyEntryNotFoundException)
        {
            errorKey = "occupancy.error.entryNotFound";
        }
        catch (InvalidPoleAttachmentSlotTransitionException)
        {
            errorKey = "occupancy.error.transition";
        }
        catch (ArgumentOutOfRangeException)
        {
            errorKey = "occupancy.error.outOfRange";
        }
        catch (ArgumentException)
        {
            errorKey = "occupancy.error.invalid";
        }
        catch (InvalidOperationException)
        {
            errorKey = "occupancy.error.slotOccupied";
        }

        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Mark(string rowKey, OccupancyChangeKind kind)
    {
        var row = Row(rowKey);
        if (row.Current is null)
        {
            // Nothing on file: the row exists only because of a pending change, which goes.
            _changes.Remove(rowKey);
            return;
        }

        _changes[rowKey] = Addressed(row, new OccupancyChange { Kind = kind });
    }

    private OccupancyDraftRow Row(string rowKey) =>
        Rows().FirstOrDefault(row => string.Equals(row.Key, rowKey, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"The draft has no row '{rowKey}'.");

    /// <summary>A change on an entry on file: by slot when it sits on one, by its id otherwise.</summary>
    private static OccupancyChange Addressed(OccupancyDraftRow row, OccupancyChange change) => row.SlotIndex is { } slotIndex
        ? change with { SlotIndex = slotIndex, EntryId = null }
        : change with { SlotIndex = null, EntryId = row.Current!.Id };

    /// <summary>
    /// The row a change belongs to. A change that names a regulated entry by id lands on that
    /// entry's slot row, so it and a change on the slot are the same row here too.
    /// </summary>
    private string KeyOf(OccupancyChange change)
    {
        if (change.SlotIndex is { } slotIndex) return SlotKey(slotIndex);

        var entryId = change.EntryId.GetValueOrDefault();
        var onSlot = _entries.FirstOrDefault(e => e.Id == entryId)?.SlotIndex;
        return onSlot is { } entrySlot && change.Kind != OccupancyChangeKind.Add ? SlotKey(entrySlot) : EntryKey(entryId);
    }

    private static string SlotKey(int slotIndex) => string.Create(CultureInfo.InvariantCulture, $"slot:{slotIndex}");

    private static string EntryKey(Guid entryId) => $"entry:{entryId:D}";

    private static int? SlotOf(string key) =>
        key.StartsWith("slot:", StringComparison.Ordinal) && int.TryParse(key.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var slotIndex)
            ? slotIndex
            : null;

    private static InvalidOperationException NoEntryId(string rowKey) =>
        new($"Row '{rowKey}' holds no entry yet; use {nameof(AddToSlot)} or {nameof(AddBeyondRegulatedSlots)} to add one.");
}
