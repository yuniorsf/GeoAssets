using GeoAssets.Workflow.Targets;

namespace GeoAssets.Shared.Services.OrderTargets;

/// <summary>
/// The working copy behind the per-target work editor (XD01-313): the activities asked on one
/// target, its status and its note, edited against the target as it was read. Every rule the
/// editor shows lives here; the host re-checks all of them on save.
///
/// <list type="bullet">
/// <item><description>
/// Activities come from the order type's catalog only, listed in catalog order. Ids the target
/// carries that the catalog no longer offers are dropped from the draft and counted in
/// <see cref="DroppedActivityCount"/>, so the editor can say so before they are saved away.
/// </description></item>
/// <item><description>
/// The statuses offered are the current one and the moves <see cref="OrderTargetStatus.CanTransition"/>
/// allows from it — a target moves one step per save.
/// </description></item>
/// <item><description>
/// A blank note is saved as no note; a note longer than <see cref="MaxNoteLength"/> cannot be saved.
/// </description></item>
/// </list>
/// </summary>
public sealed class OrderTargetWorkDraft
{
    /// <summary>The longest note the target store accepts.</summary>
    public const int MaxNoteLength = 2048;

    private readonly HashSet<string> _activityIds = new(StringComparer.OrdinalIgnoreCase);

    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public OrderTargetWorkDraft(ServiceOrderTarget target, IReadOnlyList<OrderTargetActivity> catalog)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(catalog);

        Original = target;
        Catalog = catalog;
        foreach (var id in target.ActivityIds)
        {
            if (catalog.Any(activity => string.Equals(activity.Id, id, StringComparison.OrdinalIgnoreCase))) _activityIds.Add(id);
            else DroppedActivityCount++;
        }

        Status = target.Status;
        Note = target.Note ?? string.Empty;
        StatusOptions = [.. OrderTargetStatus.All.Where(status => OrderTargetStatus.CanTransition(target.Status, status))];
    }

    /// <summary>The target as it was read; its <see cref="ServiceOrderTarget.Version"/> is the one saved against.</summary>
    public ServiceOrderTarget Original { get; }

    /// <summary>The order type's activities.</summary>
    public IReadOnlyList<OrderTargetActivity> Catalog { get; }

    /// <summary>How many of the target's activities the catalog no longer offers.</summary>
    public int DroppedActivityCount { get; }

    /// <summary>The chosen status.</summary>
    public string Status { get; set; }

    /// <summary>The note as typed.</summary>
    public string Note { get; set; }

    /// <summary>The statuses the target may be given: its own, then the moves allowed from it, in lifecycle order.</summary>
    public IReadOnlyList<string> StatusOptions { get; }

    /// <summary>The chosen activities, in catalog order and spelling.</summary>
    public IReadOnlyList<string> ActivityIds =>
        [.. Catalog.Where(activity => _activityIds.Contains(activity.Id)).Select(activity => activity.Id)];

    /// <summary>Whether the note is too long to save.</summary>
    public bool NoteTooLong => Note.Length > MaxNoteLength;

    /// <summary>The note to save: null when blank.</summary>
    public string? NoteToSave => string.IsNullOrWhiteSpace(Note) ? null : Note;

    /// <summary>
    /// Whether anything differs from <see cref="Original"/>, dropped activities included. The
    /// activities compare as a set: listing them in catalog order alone is not a change.
    /// </summary>
    public bool IsDirty =>
        DroppedActivityCount > 0
        || !string.Equals(Status, Original.Status, StringComparison.Ordinal)
        || !string.Equals(NoteToSave, Original.Note, StringComparison.Ordinal)
        || !_activityIds.SetEquals(Original.ActivityIds);

    /// <summary>Whether the draft can be saved: changed, with a status on offer and a note that fits.</summary>
    public bool CanSave => IsDirty && !NoteTooLong && StatusOptions.Contains(Status, StringComparer.Ordinal);

    /// <summary>Whether <paramref name="activityId"/> is chosen.</summary>
    public bool IsChosen(string activityId) => _activityIds.Contains(activityId);

    /// <summary>Chooses or un-chooses one catalog activity; an id the catalog does not offer is ignored.</summary>
    public void SetChosen(string activityId, bool chosen)
    {
        if (!Catalog.Any(activity => string.Equals(activity.Id, activityId, StringComparison.OrdinalIgnoreCase))) return;

        if (chosen) _activityIds.Add(activityId);
        else _activityIds.Remove(activityId);
    }

    /// <summary>
    /// The edited target to save, built through the domain's own guards, with the version it was read at.
    /// </summary>
    /// <exception cref="InvalidOperationException">The draft cannot be saved (<see cref="CanSave"/> is false).</exception>
    public ServiceOrderTarget Build()
    {
        if (!CanSave) throw new InvalidOperationException("The target's work has nothing valid to save.");

        return Original.WithActivities(ActivityIds, Catalog).WithStatus(Status) with { Note = NoteToSave };
    }
}
