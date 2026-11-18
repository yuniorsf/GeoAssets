using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

public sealed class OrderActionOutcomeTests
{
    [Fact]
    public void Applied_HasKindApplied()
    {
        OrderActionOutcome.Applied.Kind.Should().Be(OrderActionOutcomeKind.Applied);
    }

    [Fact]
    public void NotFound_HasKindNotFound()
    {
        OrderActionOutcome.NotFound.Kind.Should().Be(OrderActionOutcomeKind.NotFound);
    }

    [Fact]
    public void ConcurrentModification_HasKindConcurrentModification()
    {
        OrderActionOutcome.ConcurrentModification.Kind.Should().Be(OrderActionOutcomeKind.ConcurrentModification);
    }

    [Fact]
    public void Denied_CarriesTheVerdict()
    {
        var verdict = RuleEvaluationResult.Deny(OrderActionType.Approve, "no");

        var outcome = OrderActionOutcome.Denied(verdict);

        outcome.Kind.Should().Be(OrderActionOutcomeKind.Denied);
        outcome.Verdict.Should().BeSameAs(verdict);
    }

    [Fact]
    public void Denied_NullVerdict_Throws()
    {
        var act = () => OrderActionOutcome.Denied(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void InvalidTransition_CarriesFromAndTo()
    {
        var outcome = OrderActionOutcome.InvalidTransition("Draft", "Done");

        outcome.Kind.Should().Be(OrderActionOutcomeKind.InvalidTransition);
        outcome.From.Should().Be("Draft");
        outcome.To.Should().Be("Done");
    }

    [Fact]
    public void Problem_CarriesItsFieldsAndExtensions()
    {
        var outcome = OrderActionOutcome.Problem(409, "some.code", "A title", "A detail", ("poles", new[] { "p1" }), ("n", null));

        outcome.Kind.Should().Be(OrderActionOutcomeKind.Problem);
        outcome.StatusCode.Should().Be(409);
        outcome.Code.Should().Be("some.code");
        outcome.Title.Should().Be("A title");
        outcome.Detail.Should().Be("A detail");
        outcome.Extensions.Should().HaveCount(2);
        outcome.Extensions[0].Name.Should().Be("poles");
        outcome.Extensions[0].Value.Should().BeEquivalentTo(new[] { "p1" });
        outcome.Extensions[1].Value.Should().BeNull();
    }

    [Fact]
    public void Problem_WithoutDetailOrExtensions_HasNoneOfThem()
    {
        var outcome = OrderActionOutcome.Problem(422, "c", "t", null);

        outcome.Detail.Should().BeNull();
        outcome.Extensions.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Problem_BlankCode_Throws(string? code)
    {
        var act = () => OrderActionOutcome.Problem(400, code!, "title", null);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Problem_BlankTitle_Throws(string? title)
    {
        var act = () => OrderActionOutcome.Problem(400, "code", title!, null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NonProblemOutcomes_HaveNoProblemFields()
    {
        var outcome = OrderActionOutcome.Applied;

        outcome.Verdict.Should().BeNull();
        outcome.Code.Should().BeNull();
        outcome.StatusCode.Should().Be(0);
        outcome.Extensions.Should().BeEmpty();
    }
}
