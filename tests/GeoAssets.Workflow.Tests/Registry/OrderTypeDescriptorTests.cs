using System.Text.Json;
using FluentAssertions;
using GeoAssets.Workflow.Registry;
using Xunit;
using static GeoAssets.Workflow.Tests.Registry.RegistryTestData;

namespace GeoAssets.Workflow.Tests.Registry;

public sealed class OrderTypeDescriptorTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SameAs_EqualDescriptors_ReturnsTrue()
    {
        Descriptor(actions: [Action(), Action("isp:pole")])
            .SameAs(Descriptor(actions: [Action(), Action("isp:pole")])).Should().BeTrue();
    }

    [Fact]
    public void SameAs_ActionsInAnotherOrder_ReturnsFalse()
    {
        Descriptor(actions: [Action(), Action("isp:pole")])
            .SameAs(Descriptor(actions: [Action("isp:pole"), Action()])).Should().BeFalse();
    }

    [Fact]
    public void SameAs_DifferentActionCount_ReturnsFalse()
    {
        Descriptor(actions: Action()).SameAs(Descriptor()).Should().BeFalse();
    }

    [Theory]
    [InlineData("verticalId")]
    [InlineData("orderTypeId")]
    [InlineData("displayNameKey")]
    [InlineData("apiBaseRoute")]
    public void SameAs_AnyFieldDiffers_ReturnsFalse(string field)
    {
        var other = field switch
        {
            "verticalId" => Descriptor() with { VerticalId = "other" },
            "orderTypeId" => Descriptor() with { OrderTypeId = "other" },
            "displayNameKey" => Descriptor() with { DisplayNameKey = "other" },
            _ => Descriptor() with { ApiBaseRoute = "/other" },
        };

        Descriptor().SameAs(other).Should().BeFalse();
    }

    [Fact]
    public void SameAs_Null_Throws()
    {
        var act = () => Descriptor().SameAs(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FeatureActions_Null_BecomesEmpty()
    {
        var descriptor = Descriptor() with { FeatureActions = null! };

        descriptor.FeatureActions.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void FeatureActions_DefaultsToEmpty()
    {
        new OrderTypeDescriptor { VerticalId = "v", OrderTypeId = "o", DisplayNameKey = "k", ApiBaseRoute = "/r" }
            .FeatureActions.Should().BeEmpty();
    }

    [Fact]
    public void Json_RoundTripsWithWebDefaults()
    {
        var descriptor = Descriptor(actions: [Action(), Action("isp:pole")]);

        var json = JsonSerializer.Serialize(descriptor, Web);
        var back = JsonSerializer.Deserialize<OrderTypeDescriptor>(json, Web);

        json.Should().Contain("\"verticalId\":\"vertical-a\"").And.Contain("\"featureActions\":[");
        back!.SameAs(descriptor).Should().BeTrue();
    }

    [Fact]
    public void Json_NullFeatureActions_DeserializesToEmpty()
    {
        const string json = """{"verticalId":"v","orderTypeId":"o","displayNameKey":"k","apiBaseRoute":"/r","featureActions":null}""";

        var back = JsonSerializer.Deserialize<OrderTypeDescriptor>(json, Web);

        back!.FeatureActions.Should().BeEmpty();
    }
}
