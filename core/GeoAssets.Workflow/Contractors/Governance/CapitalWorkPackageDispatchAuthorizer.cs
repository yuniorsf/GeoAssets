namespace GeoAssets.Workflow.Contractors.Governance;

/// <summary>
/// Gates capital work package dispatch on an already-resolved contract-balance query (XD01-238
/// AC2). Deliberately a pure function over a <see cref="ContractBalanceQueryResult"/> the caller
/// already fetched — same "host does I/O, domain decides" split as
/// <see cref="EspelhoDeMedicaoDossierGenerator"/> — so the allow/deny decision itself needs no
/// repository/HTTP dependency and is trivially unit-testable.
/// </summary>
public static class CapitalWorkPackageDispatchAuthorizer
{
    /// <param name="requiredAmountBrl">
    /// The capital work package's own cost estimate that must be covered by the contract's
    /// remaining balance. Exactly equal to the remaining balance is sufficient (not denied) —
    /// only a shortfall blocks dispatch.
    /// </param>
    public static CapitalWorkPackageDispatchAuthorization Authorize(
        decimal requiredAmountBrl, ContractBalanceQueryResult balance)
    {
        ArgumentNullException.ThrowIfNull(balance);
        if (requiredAmountBrl < 0)
            throw new ArgumentOutOfRangeException(nameof(requiredAmountBrl), requiredAmountBrl, "Required amount cannot be negative.");

        return balance.RemainingBalanceBrl >= requiredAmountBrl
            ? CapitalWorkPackageDispatchAuthorization.Allow(balance.ContractId, requiredAmountBrl, balance.RemainingBalanceBrl)
            : CapitalWorkPackageDispatchAuthorization.Deny(balance.ContractId, requiredAmountBrl, balance.RemainingBalanceBrl);
    }
}
