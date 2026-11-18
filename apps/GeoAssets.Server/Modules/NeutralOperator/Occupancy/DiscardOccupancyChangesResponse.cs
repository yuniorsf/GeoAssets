namespace GeoAssets.Server.Modules.NeutralOperator.Occupancy;

/// <summary>Body of the two discard routes: how many staged change sets were discarded (0 when nothing was staged).</summary>
public sealed record DiscardOccupancyChangesResponse(int Discarded);
