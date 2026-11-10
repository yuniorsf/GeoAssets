namespace GeoAssets.FieldService.Server;

/// <summary>Request body for <see cref="ContractorGovernancePortalRestApiExtensions.MapDispatchAuthorization"/>.</summary>
internal sealed record CapitalWorkPackageDispatchAuthorizationRequest(string ContractId, decimal RequiredAmountBrl);
