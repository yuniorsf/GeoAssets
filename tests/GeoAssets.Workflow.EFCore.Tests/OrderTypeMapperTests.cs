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
}
