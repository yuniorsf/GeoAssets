using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Services;

/// <summary>
/// Singleton default implementation of <see cref="ICurrentMapContext"/>. A fixed value today
/// (mirrors <c>Index.razor</c>'s own <c>_mapDivId</c> constant) — implemented as a service
/// rather than a bare constant for DI-idiom uniformity with <see cref="IFeatureSelectionState"/>,
/// so any self-sufficient component has one consistent way to reach shared context.
/// </summary>
public sealed class CurrentMapContext : ICurrentMapContext
{
    public string MapDivId => "geoassets-map";
}
