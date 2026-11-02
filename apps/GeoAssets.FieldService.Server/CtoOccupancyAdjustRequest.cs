namespace GeoAssets.FieldService.Server;

/// <summary>
/// <see cref="CatalogInstanceId"/> is caller-supplied the same way <c>InspectServiceOrderRequest.CatalogInstanceId</c>
/// is — which Core GIS catalog instance the mutation batch targets.
/// </summary>
internal sealed record CtoOccupancyAdjustRequest(int Delta, string CatalogInstanceId);
