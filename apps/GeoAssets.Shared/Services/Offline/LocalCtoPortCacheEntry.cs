namespace GeoAssets.Shared.Services.Offline;

/// <summary>
/// A CTO's location and splitter-port occupancy, cached locally so
/// <c>GeoAssets.Workflow.FieldWork.NearestAvailableCtoAssigner</c> (XD01-273) can run a
/// nearest-available-CTO lookup with zero network connectivity (XD01-276's AC: "App functions
/// with zero network connectivity for a full drop-install workflow pass").
/// </summary>
public sealed record LocalCtoPortCacheEntry(
    string CtoFeatureId,
    double Longitude,
    double Latitude,
    int TotalPorts,
    int OccupiedPorts,
    DateTime CachedAtUtc);
