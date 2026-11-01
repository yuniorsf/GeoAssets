namespace GeoAssets.FieldService.Server;

internal sealed record InspectServiceOrderRequest(byte[] RowVersion, string CatalogInstanceId);
