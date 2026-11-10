namespace GeoAssets.FieldService.Server;

/// <summary>
/// Request body for <see cref="ContractorGovernancePortalRestApiExtensions.MapPushMeasurementSheet"/>.
/// <c>TenantSecret</c> carried in the body rather than a query string — same reasoning as
/// <c>EspelhoDeMedicaoDossierRequest</c>.
/// </summary>
internal sealed record PushMeasurementSheetRequest(string ContractId, string TenantSecret, decimal RetentionRate = 0m);
