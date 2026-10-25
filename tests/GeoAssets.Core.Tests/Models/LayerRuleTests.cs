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
