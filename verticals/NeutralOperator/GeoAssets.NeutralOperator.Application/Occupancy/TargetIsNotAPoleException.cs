namespace GeoAssets.NeutralOperator.Application.Occupancy;

/// <summary>
/// Thrown when occupancy is asked of, or staged on, a target whose feature is not of a pole
/// asset type (<see cref="OrderOccupancyOptions.PoleAssetTypeIds"/>) (XD01-315).
/// </summary>
public sealed class TargetIsNotAPoleException : InvalidOperationException
{
    public string? TargetId { get; }

    /// <summary>The asset type of the target's feature.</summary>
    public string? AssetTypeId { get; }

    public TargetIsNotAPoleException(string targetId, string assetTypeId)
        : base($"Target '{targetId}' is a feature of asset type '{assetTypeId}', which is not a pole; only poles have occupancy.")
    {
        TargetId = targetId;
        AssetTypeId = assetTypeId;
    }

    public TargetIsNotAPoleException()
    {
    }

    public TargetIsNotAPoleException(string message) : base(message)
    {
    }

    public TargetIsNotAPoleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
