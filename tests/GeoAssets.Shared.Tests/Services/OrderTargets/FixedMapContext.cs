using GeoAssets.Shared.Interfaces;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

internal sealed class FixedMapContext(string divId) : ICurrentMapContext
{
    public string MapDivId => divId;
}
