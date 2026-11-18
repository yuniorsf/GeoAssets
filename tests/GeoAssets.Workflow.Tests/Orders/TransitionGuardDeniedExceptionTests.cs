using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

public class TransitionGuardDeniedExceptionTests
{
    [Fact]
    public void VerdictConstructor_CarriesVerdictFromAndTo_AndUsesTheReasonAsMessage()
    {
        var verdict = GuardVerdict.Deny("x.denied", "not today");

        var ex = new TransitionGuardDeniedException(verdict, "A", "B");

        ex.Verdict.Should().BeSameAs(verdict);
        ex.From.Should().Be("A");
        ex.To.Should().Be("B");
        ex.Message.Should().Be("not today");
    }

    [Fact]
    public void VerdictConstructor_NullVerdict_Throws()
    {
        var act = () => new TransitionGuardDeniedException(null!, "A", "B");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void StandardConstructors_GiveAGenericDenialVerdict()
    {
        var inner = new InvalidOperationException("inner");

        new TransitionGuardDeniedException().Verdict.Allowed.Should().BeFalse();
        var withMessage = new TransitionGuardDeniedException("msg");
        withMessage.Verdict.Reason.Should().Be("msg");
        withMessage.Verdict.Code.Should().Be("workflow.transition-denied");
        var withInner = new TransitionGuardDeniedException("msg", inner);
        withInner.InnerException.Should().BeSameAs(inner);
        withInner.Verdict.Code.Should().Be("workflow.transition-denied");
    }
}
