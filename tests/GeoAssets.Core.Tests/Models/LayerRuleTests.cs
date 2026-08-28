using FluentAssertions;
using GeoAssets.Core.Models;
using Xunit;

namespace GeoAssets.Core.Tests.Models;

public class LayerRuleTests
{
    [Fact]
    public void Construction_AssignsDefaultId()
    {
        new LayerRule().Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Construction_HasExpectedDefaults()
    {
        var rule = new LayerRule();

        rule.AssetTypeId.Should().Be(Guid.Empty);
        rule.LayerId.Should().Be(Guid.Empty);
        rule.Priority.Should().Be(0);
        rule.Conditions.Should().BeEmpty();
    }

    [Fact]
    public void Construction_AssignsProvidedValues()
    {
        var id = Guid.NewGuid();
        var assetTypeId = Guid.NewGuid();
        var layerId = Guid.NewGuid();
        var condition = new LayerRuleCondition { Attribute = "voltage", Operator = LayerRuleOperator.GreaterThanOrEqual, Value = "110" };

        var rule = new LayerRule
        {
            Id = id,
            AssetTypeId = assetTypeId,
            LayerId = layerId,
            Priority = 1,
            Conditions = [condition]
        };

        rule.Id.Should().Be(id);
        rule.AssetTypeId.Should().Be(assetTypeId);
        rule.LayerId.Should().Be(layerId);
        rule.Priority.Should().Be(1);
        rule.Conditions.Should().ContainSingle().Which.Should().Be(condition);
    }

    [Fact]
    public void Conditions_MultipleEntries_AreAllRetained()
    {
        var rule = new LayerRule
        {
            Conditions =
            [
                new LayerRuleCondition { Attribute = "status", Operator = LayerRuleOperator.Equals, Value = "active" },
                new LayerRuleCondition { Attribute = "voltage", Operator = LayerRuleOperator.LessThanOrEqual, Value = "220" }
            ]
        };

        rule.Conditions.Should().HaveCount(2);
    }
}

public class LayerRuleConditionTests
{
    [Fact]
    public void Construction_HasExpectedDefaults()
    {
        var condition = new LayerRuleCondition();

        condition.Attribute.Should().BeEmpty();
        condition.Operator.Should().Be(LayerRuleOperator.Equals);
        condition.Value.Should().BeEmpty();
    }

    [Theory]
    [InlineData(LayerRuleOperator.Equals)]
    [InlineData(LayerRuleOperator.NotEquals)]
    [InlineData(LayerRuleOperator.GreaterThanOrEqual)]
    [InlineData(LayerRuleOperator.LessThanOrEqual)]
    public void Construction_AssignsProvidedOperator(LayerRuleOperator op)
    {
        var condition = new LayerRuleCondition { Attribute = "material", Operator = op, Value = "steel" };

        condition.Attribute.Should().Be("material");
        condition.Operator.Should().Be(op);
        condition.Value.Should().Be("steel");
    }
}
