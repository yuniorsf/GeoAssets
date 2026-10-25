using FluentAssertions;
using GeoAssets.Core.Models;
using Xunit;

namespace GeoAssets.Core.Tests.Models;

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
