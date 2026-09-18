using GeoAssets.Core.Models;

namespace GeoAssets.Shared.Components.Map;

public enum CandidatePickerMode { Single, Multiple }

/// <summary>
/// Pure interaction-state machine behind the multi-candidate "Connect to…" picker (XD01-152),
/// implementing the design settled in XD01-151: clicking the newly-placed asset cycles the
/// current candidate circularly; Single mode confirms only the current candidate; Multiple mode
/// toggles candidates into a selection set, and switching Single → Multiple carries the current
/// candidate over as already selected. Factored out of <see cref="CandidatePicker"/> so it's
/// directly unit-testable without a Blazor render tree (this repo has no bUnit — same reasoning
/// as <c>AssetForm.FindAutoLinkCandidate</c>/<c>MapContainer.ResolveDrawnAssetTypeId</c>).
/// </summary>
public sealed class CandidatePickerState
{
    private readonly HashSet<string> _selectedIds = [];

    public IReadOnlyList<GeoFeature> Candidates { get; }
    public int CurrentIndex { get; private set; }
    public CandidatePickerMode Mode { get; private set; } = CandidatePickerMode.Single;

    public CandidatePickerState(IReadOnlyList<GeoFeature> candidates)
    {
        if (candidates.Count == 0)
            throw new ArgumentException("At least one candidate is required.", nameof(candidates));
        Candidates = candidates;
    }

    public GeoFeature Current => Candidates[CurrentIndex];

    public int SelectedCount => _selectedIds.Count;

    public bool IsSelected(GeoFeature candidate) => _selectedIds.Contains(candidate.Id);

    /// <summary>Advances to the next candidate, wrapping circularly from the last back to the first.</summary>
    public void CycleNext() => CurrentIndex = (CurrentIndex + 1) % Candidates.Count;

    /// <summary>
    /// Switches interaction mode. Entering Multiple mode carries the current candidate over into
    /// the selection set (least-surprising — avoids losing the choice already zeroed in on).
    /// </summary>
    public void SwitchMode(CandidatePickerMode mode)
    {
        Mode = mode;
        if (mode == CandidatePickerMode.Multiple)
            _selectedIds.Add(Current.Id);
    }

    /// <summary>Multiple mode only: adds or removes the current candidate from the selection set.</summary>
    public void ToggleCurrentSelected()
    {
        if (!_selectedIds.Remove(Current.Id))
            _selectedIds.Add(Current.Id);
    }

    /// <summary>
    /// The candidate(s) to link on confirm — the current one in Single mode, the selection set
    /// (in candidate order) in Multiple mode.
    /// </summary>
    public IReadOnlyList<GeoFeature> Confirm() => Mode == CandidatePickerMode.Single
        ? [Current]
        : [.. Candidates.Where(c => _selectedIds.Contains(c.Id))];
}
