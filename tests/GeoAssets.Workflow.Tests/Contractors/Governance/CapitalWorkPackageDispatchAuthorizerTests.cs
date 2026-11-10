using FluentAssertions;
using GeoAssets.Workflow.Contractors.Governance;
using Xunit;

namespace GeoAssets.Workflow.Tests.Contractors.Governance;

public class CapitalWorkPackageDispatchAuthorizerTests
{
    private static ContractBalanceQueryResult Balance(decimal remainingBalanceBrl, string contractId = "contract-1") =>
        new(contractId, remainingBalanceBrl, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void Authorize_SufficientBalance_Allows()
    {
        var result = CapitalWorkPackageDispatchAuthorizer.Authorize(1_000m, Balance(5_000m));

        result.Allowed.Should().BeTrue();
        result.RequiredAmountBrl.Should().Be(1_000m);
        result.RemainingBalanceBrl.Should().Be(5_000m);
    }

    [Fact]
    public void Authorize_InsufficientBalance_Denies()
    {
        var result = CapitalWorkPackageDispatchAuthorizer.Authorize(5_000m, Balance(1_000m));

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Contain("insufficient");
    }

    [Fact]
    public void Authorize_ExactlyEqualBalance_Allows()
    {
        var result = CapitalWorkPackageDispatchAuthorizer.Authorize(1_000m, Balance(1_000m));

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public void Authorize_NegativeRequiredAmount_Throws()
    {
        var act = () => CapitalWorkPackageDispatchAuthorizer.Authorize(-1m, Balance(1_000m));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Authorize_NullBalance_Throws()
    {
        var act = () => CapitalWorkPackageDispatchAuthorizer.Authorize(1m, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Authorize_PropagatesContractId()
    {
        var result = CapitalWorkPackageDispatchAuthorizer.Authorize(1m, Balance(10m, "contract-42"));

        result.ContractId.Should().Be("contract-42");
    }
}
