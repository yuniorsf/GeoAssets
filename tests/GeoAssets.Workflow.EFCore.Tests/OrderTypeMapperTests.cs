using System.Text.Json;
using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Entities;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

public class OrderTypeMapperTests
{
    private const string AttributesSchema = """{"type":"object"}""";

    private static OrderTargetPolicy FullTargetPolicy(OrderTargetFanOutPolicy? fanOut) => new()
    {
        TargetAssetTypeIds = ["pole", "manhole"],
        AliasAttributeKey = "tag",
        SelectionLimit = 500,
        Activities = [new("inspect", "Inspect"), new("replace", "Replace")],
        Exclusivity = OrderTargetExclusivity.WarnOnOverlap,
        FanOut = fanOut,
    };

    private static OrderType TypeWith(OrderTargetPolicy? policy, string? attributesSchemaJson = null) => new()
    {
        Id = "pole-inspection",
        DisplayName = "Pole inspection",
        AttributesSchemaJson = attributesSchemaJson,
        TargetPolicy = policy,
    };

    // ── OrderTypeMapper ───────────────────────────────────────────────────────

    [Fact]
    public void ToRecord_NoPolicyNoSchema_LeavesBothColumnsNull()
    {
        var record = OrderTypeMapper.ToRecord(TypeWith(policy: null));

        record.TargetPolicyJson.Should().BeNull();
        record.AttributesSchemaJson.Should().BeNull();
    }

    [Fact]
    public void ToRecord_CopiesAttributesSchemaJsonVerbatim()
    {
        OrderTypeMapper.ToRecord(TypeWith(policy: null, AttributesSchema)).AttributesSchemaJson.Should().Be(AttributesSchema);
    }

    [Fact]
    public void ToDomain_CopiesAttributesSchemaJsonVerbatim()
    {
        var record = new OrderTypeRecord { Id = "inspection", DisplayName = "Inspection", AttributesSchemaJson = AttributesSchema };

        OrderTypeMapper.ToDomain(record).AttributesSchemaJson.Should().Be(AttributesSchema);
    }

    [Fact]
    public void ToDomain_RecordWithoutEitherColumn_LeavesBothNull()
    {
        var domain = OrderTypeMapper.ToDomain(new OrderTypeRecord { Id = "inspection", DisplayName = "Inspection" });

        domain.TargetPolicy.Should().BeNull();
        domain.AttributesSchemaJson.Should().BeNull();
    }

    [Fact]
    public void ToRecord_ThenToDomain_RoundTripsFullPolicyWithFanOut()
    {
        var policy = FullTargetPolicy(new OrderTargetFanOutPolicy("pole-inspection-notice"));

        var roundTripped = OrderTypeMapper.ToDomain(OrderTypeMapper.ToRecord(TypeWith(policy, AttributesSchema)));

        roundTripped.TargetPolicy.Should().BeEquivalentTo(policy);
        roundTripped.TargetPolicy!.FanOut!.PartyKind.Should().Be(OrderTargetFanOutPartyKind.OccupantOrganization);
        roundTripped.AttributesSchemaJson.Should().Be(AttributesSchema);
    }

    [Fact]
    public void ToRecord_ThenToDomain_RoundTripsPolicyWithoutFanOut()
    {
        var policy = FullTargetPolicy(fanOut: null);

        var roundTripped = OrderTypeMapper.ToDomain(OrderTypeMapper.ToRecord(TypeWith(policy)));

        roundTripped.TargetPolicy.Should().BeEquivalentTo(policy);
        roundTripped.TargetPolicy!.FanOut.Should().BeNull();
    }

    [Fact]
    public void ToRecord_ThenToDomain_MinimalPolicy_KeepsDefaults()
    {
        var policy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"] };

        var roundTripped = OrderTypeMapper.ToDomain(OrderTypeMapper.ToRecord(TypeWith(policy))).TargetPolicy;

        roundTripped!.TargetAssetTypeIds.Should().Equal("pole");
        roundTripped.AliasAttributeKey.Should().BeNull();
        roundTripped.SelectionLimit.Should().BeNull();
        roundTripped.Activities.Should().BeEmpty();
        roundTripped.Exclusivity.Should().Be(OrderTargetExclusivity.None);
        roundTripped.FanOut.Should().BeNull();
    }

    // ── OrderTargetPolicyMapper ───────────────────────────────────────────────

    [Fact]
    public void ToJson_NullPolicy_ReturnsNull()
    {
        OrderTargetPolicyMapper.ToJson(null).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromJson_NullOrBlank_ReturnsNull(string? json)
    {
        OrderTargetPolicyMapper.FromJson(json).Should().BeNull();
    }

    [Fact]
    public void ToJson_WritesCamelCasePropertiesAndEnumsByName()
    {
        var json = OrderTargetPolicyMapper.ToJson(FullTargetPolicy(new OrderTargetFanOutPolicy("pole-inspection-notice")));

        using var document = JsonDocument.Parse(json!);
        var root = document.RootElement;
        root.GetProperty("targetAssetTypeIds").EnumerateArray().Select(e => e.GetString()).Should().Equal("pole", "manhole");
        root.GetProperty("aliasAttributeKey").GetString().Should().Be("tag");
        root.GetProperty("selectionLimit").GetInt32().Should().Be(500);
        root.GetProperty("activities")[1].GetProperty("id").GetString().Should().Be("replace");
        root.GetProperty("activities")[1].GetProperty("displayName").GetString().Should().Be("Replace");
        root.GetProperty("exclusivity").GetString().Should().Be("WarnOnOverlap");
        root.GetProperty("fanOut").GetProperty("childOrderTypeId").GetString().Should().Be("pole-inspection-notice");
        root.GetProperty("fanOut").GetProperty("partyKind").GetString().Should().Be("OccupantOrganization");
    }

    [Fact]
    public void FromJson_DocumentWithOnlyTargetAssetTypeIds_AppliesDefaults()
    {
        var policy = OrderTargetPolicyMapper.FromJson("""{"targetAssetTypeIds":["pole"]}""");

        policy!.TargetAssetTypeIds.Should().Equal("pole");
        policy.Activities.Should().BeEmpty();
        policy.Exclusivity.Should().Be(OrderTargetExclusivity.None);
        policy.FanOut.Should().BeNull();
    }

    [Fact]
    public void FromJson_FanOutWithoutPartyKind_DefaultsToOccupantOrganization()
    {
        var policy = OrderTargetPolicyMapper.FromJson(
            """{"targetAssetTypeIds":["pole"],"fanOut":{"childOrderTypeId":"notice"}}""");

        policy!.FanOut.Should().Be(new OrderTargetFanOutPolicy("notice", OrderTargetFanOutPartyKind.OccupantOrganization));
    }

    [Fact]
    public void FromJson_MissingRequiredTargetAssetTypeIds_ThrowsJsonException()
    {
        var act = () => OrderTargetPolicyMapper.FromJson("""{"selectionLimit":5}""");

        act.Should().Throw<JsonException>();
    }

    // ── RecipientAccess (XD01-333) ────────────────────────────────────────────

    [Theory]
    [InlineData(OrderRecipientAccess.Inform, 0)]
    [InlineData(OrderRecipientAccess.Execute, 1)]
    [InlineData(OrderRecipientAccess.Edit, 2)]
    public void RecipientAccess_IsStoredAsItsNumber_AndReadBack(OrderRecipientAccess access, int stored)
    {
        var record = OrderTypeMapper.ToRecord(new OrderType { Id = "notice", DisplayName = "Notice", RecipientAccess = access });

        record.RecipientAccess.Should().Be(stored);
        OrderTypeMapper.ToDomain(record).RecipientAccess.Should().Be(access);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(7)]
    public void RecipientAccess_AStoredNumberOutsideTheEnumeration_ReadsAsInform(int stored)
    {
        var record = new OrderTypeRecord { Id = "notice", DisplayName = "Notice", RecipientAccess = stored };

        OrderTypeMapper.ToDomain(record).RecipientAccess.Should().Be(OrderRecipientAccess.Inform);
    }

    // ── RecipientActions (XD01-336) ───────────────────────────────────────────

    [Fact]
    public void RecipientActions_OfTheNotice_AreOneGrantRowPerAction_PlusOneRowPerMoveItMakes()
    {
        var notice = GeoAssets.Workflow.Isp.NeutralOperator.SharedUse.SharedUseOrderTypes.CreateOccupantNotice(["pole"]);

        OrderTypeMapper.ToRecipientActionRecords(notice)
            .Select(r => (r.OrderTypeId, r.Action, r.FromStateKey, r.ToStateKey))
            .Should().Equal(
                ("shared-use-occupant-notice", 4, (string?)null, (string?)null),
                ("shared-use-occupant-notice", 4, "Pending", "InProgress"),
                ("shared-use-occupant-notice", 9, null, null));
    }

    [Fact]
    public void RecipientActions_ATypeWithNone_HasNoRows_AndADuplicateIsWrittenOnce()
    {
        OrderTypeMapper.ToRecipientActionRecords(new OrderType { Id = "plain", DisplayName = "Plain" }).Should().BeEmpty();
        OrderTypeMapper.ToRecipientActionRecords(new OrderType
        {
            Id = "twice",
            DisplayName = "Twice",
            RecipientActions = [OrderActionType.Annotate, OrderActionType.Annotate],
        }).Should().ContainSingle();
    }

    [Fact]
    public void RecipientActions_ReadBack_AreTheDistinctActionsInWrittenOrder_AndAnUndefinedNumberIsDropped()
    {
        var stored = new List<OrderTypeRecipientActionRecord>
        {
            new() { Id = 3, OrderTypeId = "notice", Action = 9 },
            new() { Id = 1, OrderTypeId = "notice", Action = 4 },
            new() { Id = 2, OrderTypeId = "notice", Action = 4, FromStateKey = "Pending", ToStateKey = "InProgress" },
            new() { Id = 4, OrderTypeId = "notice", Action = 99 },
        };

        OrderTypeMapper.ToDomain(new OrderTypeRecord { Id = "notice", DisplayName = "Notice" }, stored).RecipientActions
            .Should().Equal(OrderActionType.Accept, OrderActionType.Annotate);
        OrderTypeMapper.ToDomain(new OrderTypeRecord { Id = "notice", DisplayName = "Notice" }).RecipientActions.Should().BeEmpty();
    }

    [Fact]
    public void RecipientActionsFrom_ACodeOnlyRow_ReadsTheNewVerb_ARowWithBoth_PrefersTheCode()
    {
        var stored = new List<OrderTypeRecipientActionRecord>
        {
            new() { Id = 1, OrderTypeId = "notice", Action = null, ActionCode = "Survey" },
            new() { Id = 2, OrderTypeId = "notice", Action = 9, ActionCode = "Annotate" },
            new() { Id = 3, OrderTypeId = "notice", Action = 1, ActionCode = "Survey" },
        };

        OrderTypeMapper.RecipientActionsFrom(stored).Should().Equal(new OrderAction("Survey"), OrderAction.Annotate);
    }

    [Fact]
    public void RecipientActionsFrom_ANumberOnlyRow_WrittenBeforeTheMigration_ReadsByNumber()
    {
        var stored = new List<OrderTypeRecipientActionRecord>
        {
            new() { Id = 1, OrderTypeId = "notice", Action = 13, ActionCode = null },
            new() { Id = 2, OrderTypeId = "notice", Action = 4, ActionCode = null },
        };

        OrderTypeMapper.RecipientActionsFrom(stored).Should().Equal(OrderActionAliases.FromNumber(13), OrderAction.Accept);
    }

    [Fact]
    public void RecipientActionsFrom_ARowWithNeitherACodeNorAKnownNumber_IsDropped()
    {
        var stored = new List<OrderTypeRecipientActionRecord>
        {
            new() { Id = 1, OrderTypeId = "notice", Action = null, ActionCode = null },
            new() { Id = 2, OrderTypeId = "notice", Action = 99, ActionCode = null },
            new() { Id = 3, OrderTypeId = "notice", Action = null, ActionCode = "  " },
            new() { Id = 4, OrderTypeId = "notice", Action = 4, ActionCode = null },
        };

        OrderTypeMapper.RecipientActionsFrom(stored).Should().Equal(OrderAction.Accept);
    }

    [Fact]
    public void ToRecipientActionRecords_AHistoricalAction_WritesItsNumberAndCode_ANewVerb_OnlyItsCode()
    {
        var records = OrderTypeMapper.ToRecipientActionRecords(new OrderType
        {
            Id = "t",
            DisplayName = "T",
            RecipientActions = [OrderAction.Annotate, new OrderAction("Survey")],
        });

        records.Select(r => (r.Action, r.ActionCode)).Should().Equal(((int?)9, (string?)"Annotate"), ((int?)null, (string?)"Survey"));
    }
}
