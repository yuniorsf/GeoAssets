using GeoAssets.Workflow.Occupancy;

namespace GeoAssets.Server.Modules.NeutralOperator;

/// <summary>
/// Warns once at startup that the host still names pole asset types under the deprecated
/// <see cref="OrderOccupancyHostExtensions.PoleAssetTypeIdsKey"/> (XD01-368) — honoured, but the
/// <see cref="OccupancyAssetTypeRoles.OccupiableSupport"/> asset-type role is what says which types are poles.
/// </summary>
public sealed partial class DeprecatedPoleAssetTypeIdsWarning(
    ILogger<DeprecatedPoleAssetTypeIdsWarning> logger, IReadOnlyList<string> poleAssetTypeIds) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        LogDeprecated(logger, string.Join(", ", poleAssetTypeIds));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(
        EventId = 3680,
        Level = LogLevel.Warning,
        Message = "Configuration key '" + OrderOccupancyHostExtensions.PoleAssetTypeIdsKey + "' is deprecated; tag the asset types {PoleAssetTypeIds} with the '" +
                  OccupancyAssetTypeRoles.OccupiableSupport + "' role instead. They are still treated as poles.")]
    private static partial void LogDeprecated(ILogger logger, string poleAssetTypeIds);
}
