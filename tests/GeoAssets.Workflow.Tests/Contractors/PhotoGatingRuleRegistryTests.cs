using FluentAssertions;
using GeoAssets.Workflow.Contractors;
using Xunit;

namespace GeoAssets.Workflow.Tests.Contractors;

public class PhotoGatingRuleRegistryTests
{
    [Fact]
    public void Find_UnregisteredItemCode_ReturnsNull()
    {
        var registry = new PhotoGatingRuleRegistry();

        registry.Find("US-EL-014").Should().BeNull();
    }

    [Fact]
    public void Register_ThenFind_ReturnsTheRule()
    {
        var registry = new PhotoGatingRuleRegistry();
        var rule = new PhotoGatingRule("US-EL-014", new HashSet<PhotoGatingStage> { PhotoGatingStage.Before, PhotoGatingStage.After });

        registry.Register(rule);

        registry.Find("US-EL-014").Should().Be(rule);
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        var registry = new PhotoGatingRuleRegistry();
        registry.Register(new PhotoGatingRule("US-EL-014", new HashSet<PhotoGatingStage> { PhotoGatingStage.Before }));

        registry.Find("us-el-014").Should().NotBeNull();
    }

    [Fact]
    public void Register_NullRule_Throws()
    {
        var registry = new PhotoGatingRuleRegistry();

        var act = () => registry.Register(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
