using GeoAssets.Core.Interfaces;

namespace GeoAssets.Commands.Contracts;

/// <summary>
/// Passed to every handler execution — provides access to core GIS services
/// without coupling handlers to specific implementations.
/// </summary>
public sealed class GeoCommandContext(IAssetRepository repository)
{
    public IAssetRepository Repository { get; } = repository;
}
