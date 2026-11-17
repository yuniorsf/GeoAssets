using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>Records feature-state pushes; a push can be made to wait or fail.</summary>
internal sealed class RecordingStateInterop : IMapSelectionInterop
{
    public List<(string DivId, string[] Ids, MapFeatureState State)> Calls { get; } = [];

    /// <summary>Called after a push is recorded; may wait or throw.</summary>
    public Func<MapFeatureState, Task>? OnPush { get; set; }

    public async Task SetFeatureStatesAsync(string divId, IReadOnlyCollection<string> featureIds, MapFeatureState state)
    {
        Calls.Add((divId, [.. featureIds], state));
        if (OnPush is not null) await OnPush(state);
    }

    public Task ClearFeatureStatesAsync(string divId) => throw new NotSupportedException("the order's states never clear the whole map");

    public Task SetSelectionToolAsync(string divId, MapSelectionTool tool) => throw new NotSupportedException();

    public Task ShowSelectionOutlineAsync(string divId, double[][][] rings) => throw new NotSupportedException();

    public Task ClearSelectionOutlineAsync(string divId) => throw new NotSupportedException();
}
