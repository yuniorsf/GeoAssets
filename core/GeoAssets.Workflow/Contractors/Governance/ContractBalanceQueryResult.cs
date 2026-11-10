namespace GeoAssets.Workflow.Contractors.Governance;

/// <summary>
/// Result of <see cref="IContractorGovernancePortalClient.QueryContractBalanceAsync"/> (XD01-238) —
/// the capital-works contract's remaining balance as of the portal's own query time, used to gate
/// capital work package dispatch via <see cref="CapitalWorkPackageDispatchAuthorizer"/>.
/// </summary>
public sealed record ContractBalanceQueryResult(
    string ContractId,
    decimal RemainingBalanceBrl,
    DateTime AsOfUtc);
