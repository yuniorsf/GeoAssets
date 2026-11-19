using FluentAssertions;
using GeoAssets.Workflow.Registry;
using GeoAssets.Workflow.Targets;
using Xunit;
using static GeoAssets.Workflow.Tests.Registry.RegistryTestData;

namespace GeoAssets.Workflow.Tests.Registry;

/// <summary>XD01-379: a descriptor's published <c>TargetPolicy</c> and <c>TerminalStatuses</c>.</summary>
public sealed class OrderTypeDescriptorTargetPolicyValidationTests
{
    private static OrderTargetPolicy Policy() => new() { TargetAssetTypeIds = ["pole"], Exclusivity = OrderTargetExclusivity.BlockOnOverlap };

    public static TheoryData<string, OrderTargetPolicy, string> InvalidPolicies => new()
    {
        { "no asset types", new OrderTargetPolicy(), "at least one target asset type" },
        { "malformed role", new OrderTargetPolicy { TargetAssetTypeRoles = ["Not A Role"] }, "not a valid role" },
        { "zero selection limit", new OrderTargetPolicy { TargetAssetTypeIds = ["pole"], SelectionLimit = 0 }, "selection limit" },
        { "blank alias key", new OrderTargetPolicy { TargetAssetTypeIds = ["pole"], AliasAttributeKey = " " }, "alias attribute key" },
        {
            "duplicate activity",
            new OrderTargetPolicy
            {
                TargetAssetTypeIds = ["pole"],
                Activities = [new OrderTargetActivity("a", "labels.a"), new OrderTargetActivity("A", "labels.b")],
            },
            "more than once"
        },
    };

    [Theory]
    [MemberData(nameof(InvalidPolicies))]
    public void FindProblem_InvalidTargetPolicy_ReturnsTheMessage(string scenario, OrderTargetPolicy policy, string fragment)
    {
        var descriptor = Descriptor() with { TargetPolicy = policy };

        OrderTypeDescriptorValidation.FindProblem("vertical-a", [descriptor]).Should().NotBeNull().And.Contain(fragment, scenario);
    }

    [Fact]
    public void FindProblem_ValidTargetPolicyAndTerminalStatuses_IsNull()
    {
        var descriptor = Descriptor() with { TargetPolicy = Policy(), TerminalStatuses = ["Done", "Cancelled"] };

        OrderTypeDescriptorValidation.FindProblem("vertical-a", [descriptor]).Should().BeNull();
    }

    [Fact]
    public void FindProblem_NoTargetPolicyNoTerminalStatuses_IsNull()
    {
        OrderTypeDescriptorValidation.FindProblem("vertical-a", [Descriptor()]).Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void FindProblem_BlankTerminalStatus_ReturnsAMessage(string blank)
    {
        var descriptor = Descriptor() with { TerminalStatuses = ["Done", blank] };

        OrderTypeDescriptorValidation.FindProblem("vertical-a", [descriptor]).Should().Contain("blank terminal status");
    }

    [Fact]
    public void TerminalStatuses_Null_BecomesEmpty()
    {
        (Descriptor() with { TerminalStatuses = null! }).TerminalStatuses.Should().BeEmpty();
    }

    [Fact]
    public void SameAs_DiffersByTargetPolicyOrTerminalStatuses_AndIgnoresReferenceIdentity()
    {
        var baseline = Descriptor() with { TargetPolicy = Policy(), TerminalStatuses = ["Done"] };

        baseline.SameAs(Descriptor() with { TargetPolicy = Policy(), TerminalStatuses = ["Done"] }).Should().BeTrue("equal policies are the same even as different instances");
        baseline.SameAs(Descriptor() with { TargetPolicy = Policy() with { Exclusivity = OrderTargetExclusivity.None }, TerminalStatuses = ["Done"] }).Should().BeFalse();
        baseline.SameAs(Descriptor() with { TargetPolicy = null, TerminalStatuses = ["Done"] }).Should().BeFalse();
        baseline.SameAs(Descriptor() with { TargetPolicy = Policy(), TerminalStatuses = ["Cancelled"] }).Should().BeFalse();
        baseline.SameAs(Descriptor() with { TargetPolicy = Policy() }).Should().BeFalse();
    }

    [Fact]
    public void Upsert_InvalidTargetPolicy_IsRejected_AndAnUnchangedRepublishReportsNoChange()
    {
        var registry = new InMemoryOrderTypeDescriptorRegistry([]);
        var valid = Descriptor() with { TargetPolicy = Policy(), TerminalStatuses = ["Done"] };

        var invalid = () => registry.Upsert("vertical-a", [Descriptor() with { TargetPolicy = new OrderTargetPolicy() }]);

        invalid.Should().Throw<ArgumentException>();
        registry.Upsert("vertical-a", [valid]).Should().BeTrue();
        registry.Upsert("vertical-a", [Descriptor() with { TargetPolicy = Policy(), TerminalStatuses = ["Done"] }]).Should().BeFalse();
        registry.GetAll().Single().TargetPolicy!.Exclusivity.Should().Be(OrderTargetExclusivity.BlockOnOverlap);
    }
}
