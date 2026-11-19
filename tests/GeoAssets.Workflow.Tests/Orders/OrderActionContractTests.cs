using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

/// <summary>
/// XD01-366: <see cref="OrderAction"/> replaces the closed <see cref="OrderActionType"/> in the action log and on the wire.
/// Every historical value must keep its number, its code and its JSON bytes — what is stored or sent today reads identically.
/// </summary>
public class OrderActionContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

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
    public void TheEnumerationHasNotChanged_SoEveryNumberIsCovered() =>
        Enum.GetValues<OrderActionType>().Select(v => (int)v).Should().Equal(Enumerable.Range(0, 14));

    [Theory]
    [MemberData(nameof(HistoricalValues))]
    public void EveryHistoricalValue_ConvertsToTheActionWithItsNumber(OrderActionType value)
    {
        OrderAction action = value;

        OrderActionAliases.NumberOf(action).Should().Be((int)value);
        action.Code.Should().Be(value.ToString());
        OrderAction.FromOrderActionType(value).Should().Be(action);
        OrderActionAliases.FromNumber((int)value).Should().Be(action);
    }

    [Theory]
    [MemberData(nameof(HistoricalValues))]
    public void Json_EveryHistoricalValue_WritesTheSameNumberTheEnumWrote(OrderActionType value)
    {
        OrderAction action = value;

        JsonSerializer.Serialize(action).Should().Be(JsonSerializer.Serialize(value));
        JsonSerializer.Serialize(action, GeoJsonSerializer.Options).Should().Be(JsonSerializer.Serialize(value, GeoJsonSerializer.Options));
        JsonSerializer.Serialize(action).Should().Be(((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [MemberData(nameof(HistoricalValues))]
    public void Json_EveryHistoricalValue_ReadsFromNumberAndFromString(OrderActionType value)
    {
        OrderAction expected = value;

        JsonSerializer.Deserialize<OrderAction>(((int)value).ToString(System.Globalization.CultureInfo.InvariantCulture)).Should().Be(expected);
        JsonSerializer.Deserialize<OrderAction>($"\"{value}\"").Should().Be(expected);
        JsonSerializer.Deserialize<OrderAction>(JsonSerializer.Serialize(value)).Should().Be(expected);
    }

    [Fact]
    public void Json_NewVerb_RoundTripsAsString()
    {
        var survey = new OrderAction("Survey");

        var json = JsonSerializer.Serialize(survey);

        json.Should().Be("\"Survey\"");
        JsonSerializer.Deserialize<OrderAction>(json).Should().Be(survey);
        JsonSerializer.Serialize(survey, GeoJsonSerializer.Options).Should().Be("\"Survey\"");
    }

    [Theory]
    [InlineData("14")]
    [InlineData("-1")]
    [InlineData("99")]
    [InlineData("1.5")]
    public void Json_UnknownNumber_Throws(string json)
    {
        var act = () => JsonSerializer.Deserialize<OrderAction>(json);

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData("\"\"")]
    [InlineData("\"   \"")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("true")]
    [InlineData("[]")]
    public void Json_BlankOrNull_Throws(string json)
    {
        var act = () => JsonSerializer.Deserialize<OrderAction>(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Json_Default_CannotBeWritten()
    {
        var act = () => JsonSerializer.Serialize(default(OrderAction));

        act.Should().Throw<JsonException>();
    }

    [Theory]
    [InlineData(13, "Dispute")]
    [InlineData(1, "Approve")]
    [InlineData(0, "View")]
    [InlineData(10, "Calculate")]
    public void Json_OrderActionLog_ExistingPayload_ReadsIdentically(int number, string code)
    {
        var payload =
            $$"""{"action":{{number}},"performedBy":"user-1","performedAt":"2026-01-15T10:30:00Z","comment":"note","resultingStatus":"Pending","actorKind":0,"agentInvocationId":null}""";

        var log = JsonSerializer.Deserialize<OrderActionLog>(payload, Web)!;

        log.Action.Code.Should().Be(code);
        OrderActionAliases.NumberOf(log.Action).Should().Be(number);
        log.PerformedBy.Should().Be("user-1");
        log.Comment.Should().Be("note");
        log.ResultingStatus.Should().Be("Pending");
        JsonSerializer.Serialize(log, Web).Should().Be(payload);
    }

    [Fact]
    public void Json_OrderActionLog_ACodedPayload_ReadsTheSameActionAsTheNumberedOne()
    {
        const string Numbered = """{"action":9,"performedBy":"u","performedAt":"2026-01-15T10:30:00Z"}""";
        const string Coded = """{"action":"Annotate","performedBy":"u","performedAt":"2026-01-15T10:30:00Z"}""";

        JsonSerializer.Deserialize<OrderActionLog>(Coded, Web)!.Action
            .Should().Be(JsonSerializer.Deserialize<OrderActionLog>(Numbered, Web)!.Action);
    }

    [Fact]
    public void Equality_IsOrdinalOnCode()
    {
        new OrderAction("Survey").Should().Be(new OrderAction("Survey"));
        (new OrderAction("Survey") == new OrderAction("Survey")).Should().BeTrue();
        new OrderAction("Survey").GetHashCode().Should().Be(new OrderAction("Survey").GetHashCode());
        new OrderAction("Survey").Should().NotBe(new OrderAction("survey"));
        (new OrderAction("Survey") != new OrderAction("SURVEY")).Should().BeTrue();
        OrderAction.Approve.Should().Be(new OrderAction("Approve"));
        OrderAction.Approve.Should().Be((OrderAction)OrderActionType.Approve);
    }

    [Fact]
    public void Default_IsEmpty()
    {
        var action = default(OrderAction);

        action.IsEmpty.Should().BeTrue();
        action.Code.Should().BeEmpty();
        action.ToString().Should().BeEmpty();
        OrderActionAliases.NumberOf(action).Should().BeNull();
        new OrderAction("Survey").IsEmpty.Should().BeFalse();
        new OrderAction("Survey").ToString().Should().Be("Survey");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlank(string code)
    {
        var act = () => new OrderAction(code);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsNull()
    {
        var act = () => new OrderAction(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(14)]
    [InlineData(int.MaxValue)]
    public void Aliases_FromNumber_UnknownThrows(int number)
    {
        var act = () => OrderActionAliases.FromNumber(number);

        act.Should().Throw<ArgumentOutOfRangeException>();
        OrderActionAliases.TryFromNumber(number, out var action).Should().BeFalse();
        action.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Aliases_NumberOf_NewVerbIsNull() =>
        OrderActionAliases.NumberOf(new OrderAction("Survey")).Should().BeNull();

    [Fact]
    public void Aliases_All_ListsEveryHistoricalNumberOnce_AndTheGenericVerbsAreTheFirstTen()
    {
        OrderActionAliases.All.Select(a => a.Number).Should().Equal(Enumerable.Range(0, 14));
        OrderAction.Generic.Should().Equal(OrderActionAliases.All.Take(10).Select(a => a.Action));
    }
}
