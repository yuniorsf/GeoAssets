using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>XD01-366: every branch of how an <see cref="OrderAction"/> is written to and read from the number and code columns.</summary>
public class StoredOrderActionTests
{
    public static TheoryData<OrderActionType> HistoricalValues()
    {
        var data = new TheoryData<OrderActionType>();
        foreach (var value in Enum.GetValues<OrderActionType>())
        {
            data.Add(value);
        }

        return data;
    }

    [Fact]
    public void CodeMaxLength_Is64() => StoredOrderAction.CodeMaxLength.Should().Be(64);

    [Theory]
    [MemberData(nameof(HistoricalValues))]
    public void Write_AHistoricalAction_FillsBothTheNumberAndTheCode(OrderActionType value)
    {
        OrderAction action = value;

        StoredOrderAction.NumberOf(action).Should().Be((int)value);
        StoredOrderAction.CodeOf(action).Should().Be(value.ToString());
    }

    [Fact]
    public void Write_ANewVerb_FillsTheCodeAndLeavesTheNumberNull()
    {
        var survey = new OrderAction("Survey");

        StoredOrderAction.NumberOf(survey).Should().BeNull();
        StoredOrderAction.CodeOf(survey).Should().Be("Survey");
    }

    [Fact]
    public void Write_Default_HasNoNumberAndAnEmptyCode()
    {
        StoredOrderAction.NumberOf(default).Should().BeNull();
        StoredOrderAction.CodeOf(default).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(HistoricalValues))]
    public void Read_ANumberOnlyRow_WrittenBeforeTheCodeColumnExisted_ReadsTheHistoricalAction(OrderActionType value)
    {
        StoredOrderAction.Read((int)value, null).Should().Be((OrderAction)value);
        StoredOrderAction.TryRead((int)value, null, out var action).Should().BeTrue();
        action.Should().Be((OrderAction)value);
    }

    [Theory]
    [MemberData(nameof(HistoricalValues))]
    public void Read_ACodedRowWithItsNumber_ReadsTheSameAction(OrderActionType value) =>
        StoredOrderAction.Read((int)value, value.ToString()).Should().Be((OrderAction)value);

    [Fact]
    public void Read_ACodeOnlyRow_ReadsTheNewVerb() =>
        StoredOrderAction.Read(null, "Survey").Should().Be(new OrderAction("Survey"));

    [Fact]
    public void Read_ReadsTheCodeInPreferenceToTheNumber()
    {
        StoredOrderAction.Read(1, "Survey").Should().Be(new OrderAction("Survey"));
        StoredOrderAction.Read(99, "Survey").Should().Be(new OrderAction("Survey"), "the code wins even over a number no action has");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_ABlankCode_FallsBackToTheNumber(string code) =>
        StoredOrderAction.Read(9, code).Should().Be(OrderAction.Annotate);

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "")]
    [InlineData(null, "  ")]
    [InlineData(99, null)]
    [InlineData(-1, null)]
    [InlineData(14, " ")]
    public void TryRead_NeitherACodeNorAKnownNumber_IsFalse_AndReadThrows(int? number, string? code)
    {
        StoredOrderAction.TryRead(number, code, out var action).Should().BeFalse();
        action.IsEmpty.Should().BeTrue();

        var read = () => StoredOrderAction.Read(number, code);

        read.Should().Throw<InvalidOperationException>();
    }
}
