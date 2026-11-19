using FluentAssertions;
using GeoAssets.Workflow.Registry;
using Xunit;
using static GeoAssets.Workflow.Tests.Registry.RegistryTestData;

namespace GeoAssets.Workflow.Tests.Registry;

public sealed class OrderTypeDescriptorValidationTests
{
    private static readonly Dictionary<string, (string? VerticalId, OrderTypeDescriptor?[]? Descriptors, string Fragment)> Cases = new(StringComparer.Ordinal)
    {
        ["blank vertical id"] = (" ", [Descriptor()], "vertical id is blank"),
        ["null vertical id"] = (null, [Descriptor()], "vertical id is blank"),
        ["null set"] = ("vertical-a", null, "missing"),
        ["null descriptor"] = ("vertical-a", [null], "descriptor is missing"),
        ["mismatched vertical id"] = ("vertical-a", [Descriptor("vertical-b")], "vertical-b"),
        ["blank order type id"] = ("vertical-a", [Descriptor(orderTypeId: " ")], "order type id is blank"),
        ["duplicate order type id"] = ("vertical-a", [Descriptor(), Descriptor()], "described twice"),
        ["blank display name key"] = ("vertical-a", [Descriptor() with { DisplayNameKey = "" }], "display name key"),
        ["blank api base route"] = ("vertical-a", [Descriptor() with { ApiBaseRoute = " " }], "API base route"),
        ["malformed action role"] = ("vertical-a", [Descriptor(actions: Action("Not A Role"))], "not a valid asset-type role"),
        ["blank action role"] = ("vertical-a", [Descriptor(actions: Action(""))], "not a valid asset-type role"),
        ["blank action verb"] = ("vertical-a", [Descriptor(actions: Action() with { Verb = " " })], "needs a verb"),
        ["blank action label key"] = ("vertical-a", [Descriptor(actions: Action() with { LabelKey = "" })], "needs a verb"),
        ["blank action order type id"] = ("vertical-a", [Descriptor(actions: Action(orderTypeId: " "))], "needs a verb"),
        ["blank action route"] = ("vertical-a", [Descriptor(actions: Action() with { Route = "" })], "needs a verb"),
    };

    public static TheoryData<string> Scenarios => [.. Cases.Keys];

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void FindProblem_InvalidSet_ReturnsAMessage(string scenario)
    {
        var (verticalId, descriptors, fragment) = Cases[scenario];

        var problem = OrderTypeDescriptorValidation.FindProblem(verticalId!, descriptors!);

        problem.Should().NotBeNull().And.Contain(fragment);
    }

    [Fact]
    public void FindProblem_NullAction_ReturnsAMessage()
    {
        var descriptor = Descriptor();
        descriptor = descriptor with { FeatureActions = [null!] };

        OrderTypeDescriptorValidation.FindProblem("vertical-a", [descriptor])
            .Should().Contain("feature action").And.Contain("missing");
    }

    [Fact]
    public void FindProblem_ValidSet_ReturnsNull()
    {
        var set = new[]
        {
            Descriptor(orderTypeId: "a-1", actions: [Action("occupiable-support", "a-1"), Action("isp:pole", "a-1")]),
            Descriptor(orderTypeId: "a-2"),
        };

        OrderTypeDescriptorValidation.FindProblem("vertical-a", set).Should().BeNull();
    }

    [Fact]
    public void FindProblem_EmptySet_ReturnsNull()
    {
        OrderTypeDescriptorValidation.FindProblem("vertical-a", []).Should().BeNull();
    }
}
