namespace GeoAssets.NeutralOperator.Application.Occupancy;

/// <summary>The NeutralOperator vertical's order-type capabilities (XD01-361), owned here, not by the engine.</summary>
public static class OccupancyCapabilities
{
    /// <summary>
    /// Approving an order of the type commits the occupancy staged on its targets (XD01-318), instead of a plain status write.
    /// </summary>
    public const string Commit = "occupancy-commit";
}
