using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

public class GuardVerdictTests
{
    [Fact]
    public void Allow_IsAllowed_WithoutCodeOrReason()
    {
        GuardVerdict.Allow.Allowed.Should().BeTrue();
        GuardVerdict.Allow.Code.Should().BeNull();
        GuardVerdict.Allow.Reason.Should().BeNull();
    }

    [Fact]
    public void Deny_CarriesCodeAndReason()
    {
        var verdict = GuardVerdict.Deny("x.denied", "because");

        verdict.Allowed.Should().BeFalse();
        verdict.Code.Should().Be("x.denied");
        verdict.Reason.Should().Be("because");
    }

    [Theory]
    [InlineData(null, "reason")]
    [InlineData("", "reason")]
    [InlineData("  ", "reason")]
    [InlineData("code", null)]
    [InlineData("code", "")]
    [InlineData("code", " ")]
    public void Deny_BlankArgument_Throws(string? code, string? reason)
    {
        var act = () => GuardVerdict.Deny(code!, reason!);

        act.Should().Throw<ArgumentException>();
    }
}
