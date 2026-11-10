namespace GeoAssets.Workflow.Contractors.Governance;

/// <summary>
/// Verdict from <see cref="CapitalWorkPackageDispatchAuthorizer.Authorize"/> (XD01-238 AC2).
/// </summary>
public sealed record CapitalWorkPackageDispatchAuthorization(
    bool Allowed,
    string Reason,
    string ContractId,
    decimal RequiredAmountBrl,
    decimal RemainingBalanceBrl)
{
    public static CapitalWorkPackageDispatchAuthorization Allow(
        string contractId, decimal requiredAmountBrl, decimal remainingBalanceBrl) =>
        new(Allowed: true,
            Reason: $"Contract '{contractId}' balance ({remainingBalanceBrl:F2} BRL) covers the required {requiredAmountBrl:F2} BRL.",
            contractId, requiredAmountBrl, remainingBalanceBrl);

    public static CapitalWorkPackageDispatchAuthorization Deny(
        string contractId, decimal requiredAmountBrl, decimal remainingBalanceBrl) =>
        new(Allowed: false,
            Reason: $"Contract '{contractId}' balance ({remainingBalanceBrl:F2} BRL) is insufficient for the required {requiredAmountBrl:F2} BRL.",
            contractId, requiredAmountBrl, remainingBalanceBrl);
}
