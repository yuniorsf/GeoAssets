namespace GeoAssets.FieldService.Server;

internal sealed record CtoOccupancyAdjustResponse(int NewOccupiedCount, int TotalPorts, double OccupiedPercentage);
