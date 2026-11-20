using System.Text.Json;
using FluentAssertions;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Rest;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>XD01-388: <see cref="NeutralOperatorProblemMapper"/> — every code of the vertical maps to the exception it did before the move.</summary>
public sealed class NeutralOperatorProblemMapperTests
{
    private static readonly NeutralOperatorProblemMapper Sut = new();

    private static IEnumerable<string> ConstantsOf(Type type) =>
        type.GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!);

    private static WorkflowProblem Problem(string code, string extraMembers = "")
    {
        using var document = JsonDocument.Parse(
            $$"""{ "code": "{{code}}", "detail": "Detail of {{code}}"{{(string.IsNullOrEmpty(extraMembers) ? "" : ", " + extraMembers)}} }""");
        return new WorkflowProblem(code, $"Detail of {code}", document.RootElement.Clone());
    }

    public static TheoryData<string, Type> ExpectedTypes => new()
    {
        { OccupancyProblemCodes.InvalidChange, typeof(InvalidOccupancyChangeException) },
        { OccupancyProblemCodes.EntryNotFound, typeof(OccupancyEntryNotFoundException) },
        { OccupancyProblemCodes.NotAPole, typeof(TargetIsNotAPoleException) },
        { OccupancyProblemCodes.TargetReleased, typeof(OccupancyTargetReleasedException) },
        { OccupancyProblemCodes.ConcurrentModification, typeof(OccupancyConflictException) },
        { OccupancyProblemCodes.ApplyPending, typeof(OccupancyApplyPendingException) },
        { OccupancyProblemCodes.ApplyNotPending, typeof(OccupancyApplyNotPendingException) },
        { SharedUseProblemCodes.NoFanOut, typeof(OrderTypeHasNoFanOutException) },
        { SharedUseProblemCodes.NoTargets, typeof(SharedUseDispatchBlockedException) },
        { SharedUseProblemCodes.TargetWithoutActivity, typeof(SharedUseDispatchBlockedException) },
        { SharedUseProblemCodes.AllTargetsBlocked, typeof(SharedUseDispatchBlockedException) },
        { SharedUseProblemCodes.NoOccupants, typeof(SharedUseDispatchBlockedException) },
    };

    [Theory]
    [MemberData(nameof(ExpectedTypes))]
    public void ToException_EachCodeOfTheVertical_IsItsTypedException(string code, Type expected)
    {
        Sut.ToException(Problem(code), "ord-1", OrderAction.View).Should().BeOfType(expected);
    }

    [Fact]
    public void ToException_EveryCodeConstantOfTheVertical_IsMapped_ExceptTheWarningOnlyOne()
    {
        var codes = ConstantsOf(typeof(OccupancyProblemCodes)).Concat(ConstantsOf(typeof(SharedUseProblemCodes))).ToList();

        codes.Should().NotBeEmpty();
        foreach (var code in codes.Where(code => !string.Equals(code, OccupancyProblemCodes.StaleBaseline, StringComparison.Ordinal)))
            Sut.ToException(Problem(code), "ord-1", OrderAction.View).Should().NotBeNull("code '{0}' must map to an exception", code);

        Sut.ToException(Problem(OccupancyProblemCodes.StaleBaseline), "ord-1", OrderAction.View)
            .Should().BeNull("a stale baseline is only ever a warning inside a successful read");
    }

    [Fact]
    public void ToException_EveryCodeConstant_IsCoveredByTheExpectedTypesTable()
    {
        var tabled = ExpectedTypes.Select(row => (string)row[0]).ToHashSet(StringComparer.Ordinal);
        var codes = ConstantsOf(typeof(OccupancyProblemCodes)).Concat(ConstantsOf(typeof(SharedUseProblemCodes)))
            .Where(code => !string.Equals(code, OccupancyProblemCodes.StaleBaseline, StringComparison.Ordinal));

        codes.Should().OnlyContain(code => tabled.Contains(code), "a new code needs a row (and a mapping)");
    }

    [Theory]
    [InlineData("orders.not-found")]
    [InlineData("targets.concurrent-modification")]
    [InlineData("vertical.unknown")]
    [InlineData("")]
    public void ToException_ACodeOfAnotherOwner_IsNull(string code)
    {
        Sut.ToException(Problem(code), "ord-1", OrderAction.View).Should().BeNull();
    }

    [Fact]
    public void ToException_ConcurrentModification_NamesThePoles()
    {
        var translated = Sut.ToException(Problem(OccupancyProblemCodes.ConcurrentModification, "\"poleFeatureIds\": [\"p-1\", \"p-2\"]"), "ord-1", OrderAction.Approve);

        translated.Should().BeOfType<OccupancyConflictException>().Which.PoleFeatureIds.Should().Equal("p-1", "p-2");
    }

    [Fact]
    public void ToException_ApplyPending_NamesTheOrderAndThePoles()
    {
        var translated = Sut.ToException(Problem(OccupancyProblemCodes.ApplyPending, "\"poleFeatureIds\": [\"p-1\"]"), "ord-7", OrderAction.Approve);

        var pending = translated.Should().BeOfType<OccupancyApplyPendingException>().Subject;
        pending.OrderId.Should().Be("ord-7");
        pending.PoleFeatureIds.Should().Equal("p-1");
    }

    [Theory]
    [InlineData("\"state\": \"Reverted\"", OccupancyApplySagaState.Reverted)]
    [InlineData("\"state\": \"Committed\"", OccupancyApplySagaState.Committed)]
    [InlineData("\"state\": \"nonsense\"", null)]
    [InlineData("\"state\": \"99\"", null)]
    [InlineData("", null)]
    public void ToException_ApplyNotPending_NamesTheOrderAndTheSagaState(string extra, OccupancyApplySagaState? state)
    {
        var translated = Sut.ToException(Problem(OccupancyProblemCodes.ApplyNotPending, extra), "ord-7", OrderAction.Approve);

        var notPending = translated.Should().BeOfType<OccupancyApplyNotPendingException>().Subject;
        notPending.OrderId.Should().Be("ord-7");
        notPending.State.Should().Be(state);
    }

    [Fact]
    public void ToException_WithoutAnOrderId_NamesTheEmptyOrder()
    {
        Sut.ToException(Problem(OccupancyProblemCodes.ApplyPending), null, OrderAction.Approve)
            .Should().BeOfType<OccupancyApplyPendingException>().Which.OrderId.Should().BeEmpty();
    }

    [Fact]
    public void ToException_NoFanOut_NamesTheOrderType()
    {
        Sut.ToException(Problem(SharedUseProblemCodes.NoFanOut, "\"orderTypeId\": \"plain\""), "ord-1", OrderAction.View)
            .Should().BeOfType<OrderTypeHasNoFanOutException>().Which.OrderTypeId.Should().Be("plain");
        Sut.ToException(Problem(SharedUseProblemCodes.NoFanOut), "ord-1", OrderAction.View)
            .Should().BeOfType<OrderTypeHasNoFanOutException>().Which.OrderTypeId.Should().BeEmpty();
    }

    [Fact]
    public void ToException_ABlockedDispatch_CarriesItsReasonAndTargets()
    {
        var translated = Sut.ToException(
            Problem(SharedUseProblemCodes.TargetWithoutActivity, "\"targetCount\": 130, \"targetIds\": [\"t-1\", \"t-2\"]"), "ord-1", OrderAction.Approve);

        var blocked = translated.Should().BeOfType<SharedUseDispatchBlockedException>().Subject;
        blocked.OrderId.Should().Be("ord-1");
        blocked.Reason.Should().Be(SharedUseProblemCodes.ReasonOf(SharedUseProblemCodes.TargetWithoutActivity));
        blocked.TargetIds.Should().Equal("t-1", "t-2");
        blocked.TargetCount.Should().Be(130);
    }

    [Fact]
    public void ToWriteConflictException_MapsOnlyTheConcurrentModification()
    {
        var conflict = Sut.ToWriteConflictException(Problem(OccupancyProblemCodes.ConcurrentModification, "\"poleFeatureIds\": [\"p-1\"]"), "ord-1");

        conflict.Should().BeOfType<OccupancyConflictException>().Which.PoleFeatureIds.Should().Equal("p-1");

        foreach (var code in ConstantsOf(typeof(OccupancyProblemCodes)).Concat(ConstantsOf(typeof(SharedUseProblemCodes)))
                     .Where(code => !string.Equals(code, OccupancyProblemCodes.ConcurrentModification, StringComparison.Ordinal)))
            Sut.ToWriteConflictException(Problem(code), "ord-1").Should().BeNull("code '{0}' is not a write conflict", code);
    }

    [Fact]
    public void NullProblems_Throw()
    {
        var read = () => Sut.ToException(null!, "ord-1", OrderAction.View);
        var write = () => Sut.ToWriteConflictException(null!, "ord-1");

        read.Should().Throw<ArgumentNullException>();
        write.Should().Throw<ArgumentNullException>();
    }
}
