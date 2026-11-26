using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Plugins;

public class ProviderPluginRegistryTests
{
    [Fact]
    public void All_KeepsEveryPlugin_InRegistrationOrder()
    {
        var rest = new FakePlugin("rest");
        var wfs = new FakePlugin("wfs");
        var wms = new FakePlugin("wms");

        var registry = new ProviderPluginRegistry([rest, wfs, wms]);

        registry.All.Should().Equal(rest, wfs, wms);
    }

    [Fact]
    public void All_IsASnapshot_NotTheCallersSequence()
    {
        var plugins = new List<IProviderPlugin> { new FakePlugin("rest") };

        var registry = new ProviderPluginRegistry(plugins);
        plugins.Add(new FakePlugin("wfs"));

        registry.All.Should().ContainSingle().Which.Id.Should().Be("rest");
        registry.Find("wfs").Should().BeNull();
    }

    [Fact]
    public void Empty_HasNoPlugins_AndFindsNothing()
    {
        var registry = new ProviderPluginRegistry([]);

        registry.All.Should().BeEmpty();
        registry.Find("rest").Should().BeNull();
    }

    [Theory]
    [InlineData("rest")]
    [InlineData("REST")]
    [InlineData("Rest")]
    public void Find_MatchesTheId_CaseInsensitively(string id)
    {
        var rest = new FakePlugin("rest");
        var registry = new ProviderPluginRegistry([new FakePlugin("wfs"), rest]);

        registry.Find(id).Should().BeSameAs(rest);
    }

    [Fact]
    public void Find_ReturnsNull_ForAnUnknownId()
    {
        var registry = new ProviderPluginRegistry([new FakePlugin("rest")]);

        registry.Find("wms").Should().BeNull();
        registry.Find(string.Empty).Should().BeNull();
    }

    [Fact]
    public void Constructor_FailsFast_OnTwoPluginsWithTheSameId()
    {
        var act = () => new ProviderPluginRegistry([new FakePlugin("rest"), new OtherFakePlugin("rest")]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Duplicate provider plugin id 'rest'*")
            .Which.Message.Should()
            .Contain(typeof(OtherFakePlugin).FullName)
            .And.Contain(typeof(FakePlugin).FullName);
    }

    [Fact]
    public void Constructor_FailsFast_OnIdsThatDifferOnlyInCase()
    {
        var act = () => new ProviderPluginRegistry([new FakePlugin("wms"), new FakePlugin("WMS")]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Duplicate provider plugin id 'WMS'*(id 'wms'), already registered*case-insensitive*");
    }

    [Fact]
    public void Constructor_FailsFast_OnTheSamePluginRegisteredTwice()
    {
        var rest = new FakePlugin("rest");

        var act = () => new ProviderPluginRegistry([rest, new FakePlugin("wfs"), rest]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Duplicate provider plugin id 'rest'*");
    }

    private class FakePlugin(string id) : IProviderPlugin
    {
        public string Id => id;
        public string DisplayName => id;
        public string Description => id;
        public IReadOnlyList<ProviderConfigField> ConfigFields => [];

        public Task<IAssetProvider> CreateAsync(ProviderConfig config, IServiceProvider services, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class OtherFakePlugin(string id) : FakePlugin(id);
}
