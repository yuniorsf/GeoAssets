using FluentAssertions;
using GeoAssets.Workflow.Messaging.Protobuf.V1;
using GeoAssets.Workflow.Notifications;
using Google.Protobuf;
using Xunit;
using ProtoTimestamp = Google.Protobuf.WellKnownTypes.Timestamp;

namespace GeoAssets.Workflow.Messaging.Protobuf.Tests;

/// <summary>
/// XD01-379: the wire contract of the three target-ledger events and of the optional vertical, terminal and organization
/// fields (10, 11, 12) of <see cref="OrderStateChangedEvent"/>.
/// </summary>
public sealed class TargetLedgerProtobufTests
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.Parse("c0c0c0c0-0000-4000-8000-000000000379");

    private readonly ProtobufIntegrationMessageSerializer _serializer = new();

    private static readonly TargetLedgerEventItem[] Items =
        [new("11111111-1111-1111-1111-111111111111", "feat-1"), new("22222222-2222-2222-2222-222222222222", "feat-2")];

    private static T Fill<T>(T evt, string? performedBy = "alice")
        where T : TargetLedgerEvent => evt with
        {
            VerticalId = "field-service", OrderId = "ord-1", OrganizationId = Org, Targets = Items, OccurredAt = FixedUtc, PerformedBy = performedBy,
        };

    private static TargetsReservedEvent Reserved() => Fill(new TargetsReservedEvent { VerticalId = "v", OrderId = "o", OrganizationId = Guid.Empty, Targets = [], OccurredAt = default });

    private static TargetsReleasedEvent Released() => Fill(new TargetsReleasedEvent { VerticalId = "v", OrderId = "o", OrganizationId = Guid.Empty, Targets = [], OccurredAt = default });

    private static TargetsCommittedEvent Committed() => Fill(new TargetsCommittedEvent { VerticalId = "v", OrderId = "o", OrganizationId = Guid.Empty, Targets = [], OccurredAt = default });

    private void AssertRoundTrips<T>(T expected)
        where T : TargetLedgerEvent
    {
        var actual = _serializer.Deserialize<T>(_serializer.Serialize(expected));

        actual.Should().BeOfType<T>();
        actual.VerticalId.Should().Be(expected.VerticalId);
        actual.OrderId.Should().Be(expected.OrderId);
        actual.OrganizationId.Should().Be(Org);
        actual.Targets.Should().Equal(expected.Targets);
        actual.OccurredAt.Should().Be(FixedUtc);
        actual.PerformedBy.Should().Be(expected.PerformedBy);
    }

    [Fact]
    public void TargetsReservedEvent_RoundTrips() => AssertRoundTrips(Reserved());

    [Fact]
    public void TargetsReleasedEvent_RoundTrips() => AssertRoundTrips(Released());

    [Fact]
    public void TargetsCommittedEvent_RoundTrips() => AssertRoundTrips(Committed());

    [Fact]
    public void TargetLedgerEvent_WithoutPerformedBy_RoundTripsNull()
    {
        var actual = _serializer.Deserialize<TargetsReleasedEvent>(_serializer.Serialize(Fill(Released(), performedBy: null)));

        actual.PerformedBy.Should().BeNull();
    }

    [Fact]
    public void TargetLedgerEvents_ShareOnePayloadShape_TheConcreteTypeIsTheCallers()
    {
        var bytes = _serializer.Serialize(Reserved());

        _serializer.Deserialize<TargetsReleasedEvent>(bytes).Should().BeOfType<TargetsReleasedEvent>();
        _serializer.Deserialize<TargetsCommittedEvent>(bytes).Should().BeOfType<TargetsCommittedEvent>();
    }

    [Fact]
    public void Serialize_TargetLedgerEventWithBlankVerticalOrOrder_Throws()
    {
        var noVertical = () => _serializer.Serialize(Reserved() with { VerticalId = " " });
        var noOrder = () => _serializer.Serialize(Reserved() with { OrderId = "" });

        noVertical.Should().Throw<ArgumentException>();
        noOrder.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Serialize_TargetLedgerEventWithABlankTargetPart_Throws()
    {
        var blankTarget = () => _serializer.Serialize(Reserved() with { Targets = [new TargetLedgerEventItem(" ", "feat-1")] });
        var blankFeature = () => _serializer.Serialize(Reserved() with { Targets = [new TargetLedgerEventItem("t", "")] });

        blankTarget.Should().Throw<ArgumentException>();
        blankFeature.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Deserialize_TargetLedgerPayloadMissingVerticalOrOrder_ThrowsFormatException()
    {
        var proto = new TargetLedgerEventProto
        {
            VerticalId = "", OrderId = "o", OrganizationId = Org.ToString("D"), OccurredAt = ProtoTimestamp.FromDateTimeOffset(FixedUtc),
        };

        var act = () => _serializer.Deserialize<TargetsReservedEvent>(proto.ToByteArray());

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Deserialize_TargetLedgerPayloadWithMalformedOrganization_ThrowsFormatException()
    {
        var proto = new TargetLedgerEventProto
        {
            VerticalId = "v", OrderId = "o", OrganizationId = "not-a-guid", OccurredAt = ProtoTimestamp.FromDateTimeOffset(FixedUtc),
        };

        var act = () => _serializer.Deserialize<TargetsCommittedEvent>(proto.ToByteArray());

        act.Should().Throw<FormatException>();
    }

    // ── OrderStateChangedEvent fields 10 / 11 / 12 ────────────────────────────

    private static OrderStateChangedEvent StateChanged(string? vertical, bool? isTerminal, Guid? organization) =>
        new("ord-1", "type-1", "Open", "Closed", "operator", FixedUtc, VerticalId: vertical, IsTerminal: isTerminal, OrganizationId: organization);

    [Fact]
    public void OrderStateChangedEvent_WithVerticalTerminalAndOrganization_RoundTrips()
    {
        var actual = _serializer.Deserialize<OrderStateChangedEvent>(_serializer.Serialize(StateChanged("field-service", true, Org)));

        actual.VerticalId.Should().Be("field-service");
        actual.IsTerminal.Should().BeTrue();
        actual.OrganizationId.Should().Be(Org);
    }

    [Fact]
    public void OrderStateChangedEvent_TerminalFalse_RoundTripsAsFalseNotAbsent()
    {
        var actual = _serializer.Deserialize<OrderStateChangedEvent>(_serializer.Serialize(StateChanged("v", false, null)));

        actual.IsTerminal.Should().BeFalse();
        actual.OrganizationId.Should().BeNull();
    }

    [Fact]
    public void OrderStateChangedEvent_WithoutTheNewFields_RoundTripsAllNull_AndTheWireOmitsThem()
    {
        var bytes = _serializer.Serialize(StateChanged(null, null, null));

        var actual = _serializer.Deserialize<OrderStateChangedEvent>(bytes);

        actual.VerticalId.Should().BeNull();
        actual.IsTerminal.Should().BeNull();
        actual.OrganizationId.Should().BeNull();
        var proto = OrderStateChangedEventProto.Parser.ParseFrom(bytes);
        proto.HasVerticalId.Should().BeFalse();
        proto.HasIsTerminal.Should().BeFalse();
        proto.HasOrganizationId.Should().BeFalse();
    }

    [Fact]
    public void OrderStateChangedEvent_NewFieldsUseFieldNumbers10To12()
    {
        var descriptor = OrderStateChangedEventProto.Descriptor;

        descriptor.FindFieldByName("vertical_id").FieldNumber.Should().Be(10);
        descriptor.FindFieldByName("is_terminal").FieldNumber.Should().Be(11);
        descriptor.FindFieldByName("organization_id").FieldNumber.Should().Be(12);
    }

    [Fact]
    public void OrderStateChangedEvent_OrganizationIsWrittenAsADashedGuid()
    {
        var proto = OrderStateChangedEventProto.Parser.ParseFrom(_serializer.Serialize(StateChanged("v", null, Org)));

        proto.OrganizationId.Should().Be(Org.ToString("D"));
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("c0c0c0c0000040008000000000000379")]
    public void Deserialize_OrderStateChangedWithMalformedOrganizationId_ThrowsFormatException(string malformed)
    {
        var proto = new OrderStateChangedEventProto
        {
            OrderId = "ord-1", OrderTypeId = "t", PreviousStatus = "a", NewStatus = "b", PerformedBy = "u",
            OccurredAt = ProtoTimestamp.FromDateTimeOffset(FixedUtc), OrganizationId = malformed,
        };

        var act = () => _serializer.Deserialize<OrderStateChangedEvent>(proto.ToByteArray());

        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Deserialize_OrderStateChangedFromAPublisherThatPredatesTheFields_ReadsThemAsNull()
    {
        var proto = new OrderStateChangedEventProto
        {
            OrderId = "ord-1", OrderTypeId = "t", PreviousStatus = "a", NewStatus = "b", PerformedBy = "u",
            OccurredAt = ProtoTimestamp.FromDateTimeOffset(FixedUtc),
        };

        var actual = _serializer.Deserialize<OrderStateChangedEvent>(proto.ToByteArray());

        (actual.VerticalId, actual.IsTerminal, actual.OrganizationId).Should().Be(((string?)null, (bool?)null, (Guid?)null));
    }
}
