using GeoAssets.Core.Models;
using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services;

/// <summary>Scoped default implementation of <see cref="IFeatureSelectionState"/>.</summary>
public sealed class FeatureSelectionState : IFeatureSelectionState
{
    public GeoFeature? Selected { get; private set; }
    public bool IsNew { get; private set; }

    public event Action? Changed;

    public void Select(GeoFeature feature, bool isNew = false)
    {
        Selected = feature;
        IsNew = isNew;
        Changed?.Invoke();
    }

    public void Clear()
    {
        Selected = null;
        IsNew = false;
        Changed?.Invoke();
    }
}
