using GeoAssets.Core.Models;
using GeoAssets.Shared.Interfaces;
using Microsoft.AspNetCore.Components;

namespace GeoAssets.Shared.Components.Map;

public partial class CandidatePicker
{
    [Parameter] public bool Visible { get; set; }
    [Parameter] public IReadOnlyList<GeoFeature> Candidates { get; set; } = [];
    [Parameter] public string MapDivId { get; set; } = string.Empty;
    [Parameter] public EventCallback<IReadOnlyList<GeoFeature>> OnLink { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    [Inject] private IMapInterop MapInterop { get; set; } = null!;

    private CandidatePickerState? _state;
    private IReadOnlyList<GeoFeature>? _lastCandidates;

    /// <summary>Exposes the currently-highlighted candidate so the host can clear the map
    /// highlight when it closes this picker via an abandon path (not Confirm/Cancel).</summary>
    public GeoFeature? CurrentHighlighted => _state?.Current;

    protected override void OnParametersSet()
    {
        if (Candidates.Count == 0 || ReferenceEquals(Candidates, _lastCandidates)) return;
        _lastCandidates = Candidates;
        _state = new CandidatePickerState(Candidates);
        _ = MapInterop.HighlightFeatureAsync(MapDivId, _state.Current.Id);
    }

    private string CandidateName(GeoFeature feature) =>
        string.IsNullOrEmpty(feature.Properties.Name) ? L["assets.noName"] : feature.Properties.Name;

    /// <summary>Invoked by the host when the user clicks the target asset marker on the map.</summary>
    public void CycleNext()
    {
        if (_state is null) return;
        var previous = _state.Current;
        _state.CycleNext();
        if (!ReferenceEquals(previous, _state.Current))
        {
            _ = MapInterop.ClearHighlightAsync(MapDivId, previous.Id);
            _ = MapInterop.HighlightFeatureAsync(MapDivId, _state.Current.Id);
        }
        StateHasChanged();
    }

    private void SwitchMode(CandidatePickerMode mode)
    {
        _state?.SwitchMode(mode);
        StateHasChanged();
    }

    private void ToggleCurrentSelected()
    {
        _state?.ToggleCurrentSelected();
        StateHasChanged();
    }

    private async Task Confirm()
    {
        if (_state is null) return;
        await OnLink.InvokeAsync(_state.Confirm());
    }

    private async Task Cancel() => await OnCancel.InvokeAsync();
}
